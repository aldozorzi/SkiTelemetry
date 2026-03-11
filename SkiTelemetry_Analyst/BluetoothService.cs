
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using System.Diagnostics;

namespace SkiTelemetry_Analyst
{
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    public class BluetoothService
    {
        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;

        // Chiavi per salvare gli ID permanentemente
        private const string LeftSensorKey = "LeftSensorId";
        private const string RightSensorKey = "RightSensorId";

        // UUID del Servizio (puoi inventarli, ma questi sono un buon punto di partenza)
        public static readonly Guid SkiServiceId = Guid.Parse("4fafc201-1fb5-459e-8fcc-c5c9c331914b");

        // UUID della Caratteristica che "spara" i dati
        public static readonly Guid TelemetryCharacteristicId = Guid.Parse("beb5483e-36e1-4688-b7f5-ea07361b26a8");

        // Lista dei sensori connessi (Sinistro e Destro)
        public List<IDevice> ConnectedDevices { get; } = new();

        public event EventHandler<SkiDataPoint>? DataReceived;

        // Questo dizionario tiene traccia dei sensori connessi per lato
        public Dictionary<SensorSide, IDevice> ConnectedSensors { get; } = new Dictionary<SensorSide, IDevice>();

        public BluetoothService()
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;

            // Evento che scatta quando troviamo un sensore
            // _adapter.DeviceDiscovered += OnDeviceDiscovered;
        }

        public async Task StartScanning()
        {
            if (!_ble.IsOn) return;
            await _adapter.StartScanningForDevicesAsync();
        }

        // Metodo per "accoppiare" un sensore specifico
        public void PairSensor(IDevice device, SensorSide side)
        {
            string key = (side == SensorSide.Left) ? LeftSensorKey : RightSensorKey;
            Preferences.Default.Set(key, device.Id.ToString());
        }

        // Verifica se un dispositivo è quello salvato
        private bool IsMySensor(IDevice device, SensorSide side)
        {
            string key = (side == SensorSide.Left) ? LeftSensorKey : RightSensorKey;
            string savedId = Preferences.Default.Get(key, string.Empty);

            return !string.IsNullOrEmpty(savedId) && device.Id.ToString() == savedId;
        }

        private async void OnDeviceDiscovered(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
        {
            var device = e.Device;
            if (string.IsNullOrEmpty(device.Name)) return;

            // Se è il MIO sensore sinistro
            if (IsMySensor(device, SensorSide.Left))
            {
                await ConnectToSensor(device, SensorSide.Left);
            }
            // Se è il MIO sensore destro
            else if (IsMySensor(device, SensorSide.Right))
            {
                await ConnectToSensor(device, SensorSide.Right);
            }
            // Opzionale: Se non sono accoppiati ma si chiamano SKI_L/R, 
            // potresti mostrarli in una lista per fare il primo pairing
        }

        private async Task ConnectToSensor(IDevice device, SensorSide side)
        {
            try
            {
                await _adapter.ConnectToDeviceAsync(device);
                ConnectedSensors[side] = device;

                // ECCOLO QUI: Chiamiamo finalmente il metodo che attiva i dati
                await SetupDataStreaming(device, side);

                System.Diagnostics.Debug.WriteLine($"Sincronizzato con sensore {side}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore connessione {side}: {ex.Message}");
            }
        }

        private async Task SetupDataStreaming(IDevice device, SensorSide side)
        {
            try
            {
                var service = await device.GetServiceAsync(SkiServiceId);
                if (service == null) return;

                var characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
                if (characteristic != null)
                {
                    // 1. IMPORTANTE: Rimuoviamo il metodo se era già registrato 
                    // per evitare che lo stesso dato venga processato due volte
                    characteristic.ValueUpdated -= OnCharacteristicValueChanged;

                    // 2. Registriamo il metodo
                    characteristic.ValueUpdated += OnCharacteristicValueChanged;

                    // 3. Diciamo all'ESP32 di iniziare a inviare
                    await characteristic.StartUpdatesAsync();

                    System.Diagnostics.Debug.WriteLine($"[BLE] Sottoscrizione attiva per {side}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BLE] Errore attivazione streaming: {ex.Message}");
            }
        }

        private void OnCharacteristicValueChanged(object? sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            var rawBytes = e.Characteristic.Value;
            if (rawBytes == null || rawBytes.Length < 28) return;

            var point = DecodePayload(rawBytes);
            // Cerchiamo nel dizionario quale lato corrisponde al dispositivo che ha inviato il dato
            var deviceId = e.Characteristic.Service.Device.Id;
            var sensorEntry = ConnectedSensors.FirstOrDefault(x => x.Value.Id == deviceId);
            point.Side = sensorEntry.Key;


            // Identifichiamo il lato in base al dispositivo che ha mandato il dato
            // (Possiamo farlo verificando quale sensore nel dizionario ha questa caratteristica, 
            // ma per ora manteniamo la logica semplice)
            DataReceived?.Invoke(this, point);
        }

        private SkiDataPoint DecodePayload(byte[] data)
        {
            // Verifichiamo che il pacchetto sia della dimensione corretta
            if (data.Length < 28) return new SkiDataPoint();

            return new SkiDataPoint
            {
                // Ogni ToSingle legge 4 byte
                AccX = BitConverter.ToSingle(data, 0),
                AccY = BitConverter.ToSingle(data, 4),
                AccZ = BitConverter.ToSingle(data, 8),
                GyroX = BitConverter.ToSingle(data, 12),
                GyroY = BitConverter.ToSingle(data, 16),
                GyroZ = BitConverter.ToSingle(data, 20),
                Timestamp = BitConverter.ToUInt32(data, 24)
            };
        }

        public async Task<bool> PairBothDevicesAsync()
        {
            if (_adapter.IsScanning) await _adapter.StopScanningForDevicesAsync();

            var tcs = new TaskCompletionSource<bool>();
            var foundLeft = false;
            var foundRight = false;
            bool needRight = false; // Imposta a true se usi SKI_R

            EventHandler<DeviceEventArgs>? handler = null;
            handler = async (s, e) =>
            {
                string name = e.Device.Name ?? "";
                if (name == "SKI_L" && !foundLeft)
                {
                    foundLeft = true;
                    bool ok = await ConnectAndSetup(e.Device, SensorSide.Left,tcs);
                    if (ok && (!needRight || foundRight)) tcs.TrySetResult(true);
                }
                else if (name == "SKI_R" && !foundRight)
                {
                    foundRight = true;
                    bool ok = await ConnectAndSetup(e.Device, SensorSide.Right,tcs);
                    if (ok && foundLeft) tcs.TrySetResult(true);
                }
            };

            _adapter.DeviceDiscovered += handler;
            var cts = new CancellationTokenSource(7000); // Timeout leggermente più lungo per il pairing iniziale

            try
            {
                await _adapter.StartScanningForDevicesAsync(cts.Token);
                // Aspettiamo il primo tra il successo del setup o il timeout
                await Task.WhenAny(tcs.Task, Task.Delay(7000));
            }
            catch (Exception ex) { Debug.WriteLine($"Errore: {ex.Message}"); }
            finally
            {
                await _adapter.StopScanningForDevicesAsync();
                _adapter.DeviceDiscovered -= handler;
            }

            return tcs.Task.IsCompleted && tcs.Task.Result;
        }

        private async Task<bool> ConnectAndSetup(IDevice device, SensorSide side, TaskCompletionSource<bool> tcs)
        {
            try
            {
                await _adapter.ConnectToDeviceAsync(device);
                
                ConnectedSensors[side] = device;
                Preferences.Default.Set(side == SensorSide.Left ? "LeftSensorId" : "RightSensorId", device.Id.ToString());
                var services = await device.GetServicesAsync();
                var service = await device.GetServiceAsync(Guid.Parse("4fafc201-1fb5-459e-8fcc-c5c9c331914b"));
                if (service == null)
                {
                    Debug.WriteLine($"[BLE] Errore: Servizio non trovato sul dispositivo {side}");
                    return false;
                }
                var characteristic = await service.GetCharacteristicAsync(Guid.Parse("beb5483e-36e1-4688-b7f5-ea07361b26a8"));
                if (characteristic == null)
                {
                    Debug.WriteLine($"[BLE] Errore: Caratteristica non trovata sul dispositivo {side}");
                    return false;
                }
                characteristic.ValueUpdated += (s, e) =>
                {
                    var rawBytes = e.Characteristic.Value;

                    // Se il pacchetto è vuoto o troppo corto (meno di 28 byte), esci
                    if (rawBytes == null || rawBytes.Length < 28) return;

                    // TRASFORMAZIONE DA BYTE A DATI TELEMETRICI
                    var point = new SkiDataPoint
                    {
                        // Ogni float occupa 4 byte. Leggiamo in sequenza:
                        AccX = BitConverter.ToSingle(rawBytes, 0),
                        AccY = BitConverter.ToSingle(rawBytes, 4),
                        AccZ = BitConverter.ToSingle(rawBytes, 8),
                        GyroX = BitConverter.ToSingle(rawBytes, 12),
                        GyroY = BitConverter.ToSingle(rawBytes, 16),
                        GyroZ = BitConverter.ToSingle(rawBytes, 20),
                        Timestamp = BitConverter.ToUInt32(rawBytes, 24),
                        Side = side
                    };

                    // SPARA IL DATO ALLA UI
                    DataReceived?.Invoke(this, point);
                };

                await characteristic.StartUpdatesAsync();
                tcs.TrySetResult(true);
                Debug.WriteLine($"[BLE] {side} pronto.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] Fallimento setup {side}: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectDevices()
        {
            var devices = ConnectedSensors.Values.ToList();
            ConnectedSensors.Clear(); // Puliamo subito il dizionario interno

            foreach (var device in devices)
            {
                try
                {
                    if (device != null)
                    {
                        // Timeout forzato: se non si scollega in 2 secondi, passa oltre
                        await Task.WhenAny(_adapter.DisconnectDeviceAsync(device), Task.Delay(2000));
                    }
                }
                catch { /* Ignora: il sensore potrebbe essere già spento */ }
            }
        }
    }
}
