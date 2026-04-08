
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using SkiTelemetry_Core;
using System.Diagnostics;

namespace SkiTelemetry_Analyst.Services
{
    [Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    public class BluetoothService
    {
        private static readonly Lazy<BluetoothService> _instance = new(() => new BluetoothService());
        public static BluetoothService Instance => _instance.Value;

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
        private readonly TelemetryLogger _logger = new();

        public BluetoothService()
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
            /*_adapter.DeviceDisconnected += async (s, e) =>
            {
                if (e.Device.Id == ConnectedSensor?.Id)
                {
                    ConnectedSensor = null;
                    await Task.Delay(2000); // attendi che il dispositivo sia pronto
                    await ConnectToSavedSensorAsync();
                }
            };*/
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
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - PairDevicesAsync] PairDevicesAsync");
            if (_adapter.IsScanning) await _adapter.StopScanningForDevicesAsync();
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - PairDevicesAsync] StoppedScanningForDevicesAsync");
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Errore: {ex.Message}"); }
            finally
            {
                await _adapter.StopScanningForDevicesAsync();
                _adapter.DeviceDiscovered -= handler;
            }

            return tcs.Task.IsCompleted && tcs.Task.Result;
        }

        private async Task<bool> ConnectAndSetup(IDevice device, TaskCompletionSource<bool> tcs)
        {
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - ConnectAndSetup] ConnectAndSetup");
            await DisconnectDevices();
            await Task.Delay(300);
            try
            {
                // TRUCCO: Se l'adattatore crede di essere già connesso, forza il distacco
                if (device.State == DeviceState.Connected)
                {
                    try { await _adapter.DisconnectDeviceAsync(device); } catch { }
                    await Task.Delay(500); // Un po' di respiro per il driver BT
                }
                await _adapter.ConnectToDeviceAsync(device);

                var mtu = await device.RequestMtuAsync(517);
                Console.WriteLine($"[BLE] MTU negoziato: {mtu} byte");


                ConnectedSensor = device;

                Preferences.Default.Set(SensorKey, device.Id.ToString());

                await Task.Delay(600); // lascia stabilizzare la connessione
                

                var services = await device.GetServicesAsync();
                var service = services.FirstOrDefault(s => s.Id == SkiServiceId);
                //var service = await device.GetServiceAsync(SkiServiceId);
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

            var frame = GetSkiFrameFromData(data);

            // Invia i punti uno dopo l'altro
            DataReceived?.Invoke(this, frame.Left);
            DataReceived?.Invoke(this, frame.Right);

        }

        public async Task DisconnectDevices(bool dispose = true)
        {
            if (ConnectedSensor == null) return;

            // NON toccare i servizi/caratteristiche durante il disconnect,
            // il driver BLE può andare in deadlock
            try { await _adapter.DisconnectDeviceAsync(ConnectedSensor); }
            catch { }
            finally
            {
                if (dispose) { ConnectedSensor.Dispose(); }
                ConnectedSensor = null;
            }
            /*
            if (ConnectedSensor != null)
            {
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
                try { await _adapter.DisconnectDeviceAsync(ConnectedSensor); }
                catch { }
                finally
                {
                    if (dispose)
                    {
                        ConnectedSensor.Dispose();
                        ConnectedSensor = null;
                    }
                }
            }
            ConnectedSensor = null;
            */
        }
        public async Task<bool> ConnectToSavedSensorAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - ConnectToSavedSensorAsync] ConnectToSavedSensorAsync");
            var savedId = Preferences.Default.Get(SensorKey, string.Empty);
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - ConnectToSavedSensorAsync] savedId: {savedId}");
            if (string.IsNullOrEmpty(savedId)) return false;

            if (!Guid.TryParse(savedId, out var deviceId)) return false;
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - ConnectToSavedSensorAsync] Guid parsed");
            try
            {
                if (_adapter.IsScanning)
                    await _adapter.StopScanningForDevicesAsync();

                await DisconnectDevices(dispose: false);
                await Task.Delay(300);

                var tcs = new TaskCompletionSource<bool>();
                var cts = new CancellationTokenSource(8000);
                var device = await _adapter.ConnectToKnownDeviceAsync(deviceId, new ConnectParameters(), cts.Token);

                if (device == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[BlueToothService - ConnectToSavedSensorAsync] device null");
                    // sensore non trovato → pulizia completa per la prossima volta
                    await DisconnectDevices(dispose: true);
                    Preferences.Default.Remove(SensorKey);
                    return false;
                }
                
                return await ConnectAndSetup(device, tcs);
            }
            catch (Exception ex)
            {
                await DisconnectDevices(dispose:true); 
                Preferences.Default.Remove(SensorKey);
                Debug.WriteLine($"[BLE] ConnectToSavedSensor fallito: {ex.Message}");
                return false;
            }
        }

        private SkiFrame GetSkiFrameFromData(byte[] data)
        {
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
            return new SkiFrame(leftPoint, rightPoint);
        }

        public async Task<bool> RunCalibrationAsync(int samplesCount)
        {
            System.Diagnostics.Debug.WriteLine($"[BlueToothService - RunCalibrationAsync]");
            // Se non c'è il sensore, proviamo a connetterlo
            if (ConnectedSensor == null)
            {
                System.Diagnostics.Debug.WriteLine($"[BlueToothService - RunCalibrationAsync] ConnectedSensor == null");
                var savedId = Preferences.Default.Get("MainSensorId", string.Empty);
                bool connected;
                System.Diagnostics.Debug.WriteLine($"[BlueToothService - RunCalibrationAsync] savedId ={savedId}");
                if (!string.IsNullOrEmpty(savedId))
                    connected = await ConnectToSavedSensorAsync();
                else
                    connected = await PairDevicesAsync();

                System.Diagnostics.Debug.WriteLine($"[BlueToothService - RunCalibrationAsync] connected == {connected}");
                if (!connected) return false;
            }
            if (ConnectedSensor == null)
            {
                System.Diagnostics.Debug.WriteLine($"[BlueToothService - RunCalibrationAsync] ConnectedSensor still == null");
                return false;
            }
            var service = await ConnectedSensor.GetServiceAsync(SkiServiceId);
            if(service == null)
            {
                System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] service == null");
                return false;
            }
            var characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
            if(characteristic == null)
            {
                System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] characteristic == null");
                return false;
            }

            int counter = 0;

            var tcs = new TaskCompletionSource<bool>();

            EventHandler<CharacteristicUpdatedEventArgs> handler = (s, e) =>
            {
                counter++;
                
                var frame = GetSkiFrameFromData(e.Characteristic.Value);
                //System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] receiving sample{counter}/{samplesCount} ts:{frame.Left.Timestamp}");
                //bool isSettingsLoggingEnabled = Preferences.Default.Get("IsLoggingEnabled", false);
                bool isSettingsLoggingEnabled = true; // forcing logging 
                if (!_logger.IsLogging && isSettingsLoggingEnabled)
                {
                    _logger.StartLogging("Calibrations");
                }
                else if (_logger.IsLogging)
                {
                    _logger.LogData(frame.Left);
                    _logger.LogData(frame.Right);
                }


                CalibrationService.Instance.AddFrame(frame);

                if (counter >= samplesCount) tcs.TrySetResult(true);
            };
            characteristic.ValueUpdated -= OnCharacteristicValueChanged;
            characteristic.ValueUpdated += handler;
            await characteristic.StartUpdatesAsync();

            var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            try
            {
                if (ConnectedSensor.State == DeviceState.Connected)
                {
                    await characteristic.StopUpdatesAsync();
                }
            }catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] Error: {ex.Message}");
            }


            characteristic.ValueUpdated -= handler;
            characteristic.ValueUpdated += OnCharacteristicValueChanged;

            if (result == tcs.Task)
            {
                System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] Task Complete");
                if ( _logger.IsLogging)
                {
                    _logger.StopLogging();
                }
                CalibrationService.Instance.Save();
                await DisconnectDevices(dispose: false);
                return true;
            }
            if (_logger.IsLogging)
            {
                _logger.StopLogging();
            }
            System.Diagnostics.Debug.WriteLine($"[RunCalibrationAsync] Task NOT Complete");
            await DisconnectDevices();
            return false;
        }

    }
}
