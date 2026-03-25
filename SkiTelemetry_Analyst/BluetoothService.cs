
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using SkiTelemetry_Core;
using System.Diagnostics;

namespace SkiTelemetry_Analyst
{
    [Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    public class BluetoothService
    {
        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;

        // Chiavi per salvare gli ID permanentemente
        private const string SensorKey = "MainSensorId";

        // UUID del Servizio (puoi inventarli, ma questi sono un buon punto di partenza)
        public static readonly Guid SkiServiceId = Guid.Parse("4fafc201-1fb5-459e-8fcc-c5c9c331914b");

        // UUID della Caratteristica che "spara" i dati
        public static readonly Guid TelemetryCharacteristicId = Guid.Parse("beb5483e-36e1-4688-b7f5-ea07361b26a8");

        // Lista dei sensori connessi (Sinistro e Destro)

        public event EventHandler<SkiDataPoint>? DataReceived;

        // Questo dizionario tiene traccia dei sensori connessi per lato
        public IDevice? ConnectedSensor { get; set; }

        public BluetoothState State() {  return _ble.State; }
        public BluetoothService()
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
        }

        public async Task StartScanning()
        {
            if (!_ble.IsOn) return;
            _adapter.ClearDeviceRegistries();
            await _adapter.StartScanningForDevicesAsync();
        }

        // Metodo per "accoppiare" un sensore specifico
        public void PairSensor(IDevice device)
        {
            string key = SensorKey;
            Preferences.Default.Set(key, device.Id.ToString());
        }

        public async Task<bool> PairDevicesAsync()
        {
            if (_adapter.IsScanning) await _adapter.StopScanningForDevicesAsync();

            var tcs = new TaskCompletionSource<bool>();
            var foundSensor = false;

            EventHandler<DeviceEventArgs>? handler = null;
            handler = async (s, e) =>
            {
                string name = e.Device.Name ?? "";
                if (name == "MASTER" && !foundSensor)
                {
                    foundSensor = true;
                    bool ok = await ConnectAndSetup(e.Device, tcs);
                    if (ok) tcs.TrySetResult(true);
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

        private async Task<bool> ConnectAndSetup(IDevice device, TaskCompletionSource<bool> tcs)
        {
            try
            {
                // TRUCCO: Se l'adattatore crede di essere già connesso, forza il distacco
                if (device.State == DeviceState.Connected)
                {
                    await _adapter.DisconnectDeviceAsync(device);
                    await Task.Delay(500); // Un po' di respiro per il driver BT
                }
                await _adapter.ConnectToDeviceAsync(device);

                var mtu = await device.RequestMtuAsync(517);
                Console.WriteLine($"[BLE] MTU negoziato: {mtu} byte");


                ConnectedSensor = device;

                Preferences.Default.Set(SensorKey, device.Id.ToString());
                
                var services = await device.GetServicesAsync();
                var service = await device.GetServiceAsync(SkiServiceId);
                if (service == null)
                {
                    Debug.WriteLine($"[BLE] Errore: Servizio non trovato sul dispositivo");
                    await DisconnectDevices();
                    return false;
                }
                var characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
                if (characteristic == null)
                {
                    Debug.WriteLine($"[BLE] Errore: Caratteristica non trovata sul dispositivo");
                    await DisconnectDevices();
                    return false;
                }
                Debug.WriteLine($"[BLE] binding characteristic.ValueUpdated");
                characteristic.ValueUpdated -= OnCharacteristicValueChanged;
                characteristic.ValueUpdated += OnCharacteristicValueChanged;

                try
                {
                    await characteristic.StopUpdatesAsync();
                }
                catch { /* Ignora se fallisce perché già spente */ }

                await characteristic.StartUpdatesAsync();
                tcs.TrySetResult(true);
                Debug.WriteLine($"[BLE] Sensore pronto.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] Fallimento setup: {ex.Message}");
                await DisconnectDevices();
                return false;
            }
        }

        private void OnCharacteristicValueChanged(object? s, CharacteristicUpdatedEventArgs e)
        {


            var data = e.Characteristic.Value;

            if (data == null || data.Length < 52)
            {
                Debug.WriteLine($"[BLE] Ricevuto pacchetto incompleto o nullo ({data?.Length} bytes)");
                return;
            }

            uint ts = BitConverter.ToUInt32(data, 0);

            // --- PIEDE SINISTRO (Offset 4) ---
            var leftPoint = new SkiDataPoint
            {
                Timestamp = ts,
                Side = SensorSide.Left,
                AccX = BitConverter.ToSingle(data, 4),
                AccY = BitConverter.ToSingle(data, 8),
                AccZ = BitConverter.ToSingle(data, 12),
                GyroX = BitConverter.ToSingle(data, 16),
                GyroY = BitConverter.ToSingle(data, 20),
                GyroZ = BitConverter.ToSingle(data, 24)
            };

            // --- PIEDE DESTRO (Offset 28) ---
            var rightPoint = new SkiDataPoint
            {
                Timestamp = ts,
                Side = SensorSide.Right,
                AccX = BitConverter.ToSingle(data, 28),
                AccY = BitConverter.ToSingle(data, 32),
                AccZ = BitConverter.ToSingle(data, 36),
                GyroX = BitConverter.ToSingle(data, 40),
                GyroY = BitConverter.ToSingle(data, 44),
                GyroZ = BitConverter.ToSingle(data, 48)
            };

            // Invia i punti uno dopo l'altro
            DataReceived?.Invoke(this, leftPoint);
            DataReceived?.Invoke(this, rightPoint);

        }

        public async Task DisconnectDevices()
        {
            if (ConnectedSensor != null)
            {
                // MOLTO IMPORTANTE: Sottoscrivi esplicitamente la rimozione qui
                // prima di perdere il riferimento all'oggetto
                var service = await ConnectedSensor.GetServiceAsync(SkiServiceId);
                if (service != null)
                {
                    var characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
                    if (characteristic != null)
                    {
                        characteristic.ValueUpdated -= OnCharacteristicValueChanged;
                        await characteristic.StopUpdatesAsync();
                    }
                }
                await _adapter.DisconnectDeviceAsync(ConnectedSensor);
            }
            ConnectedSensor = null;
        }
    }
}
