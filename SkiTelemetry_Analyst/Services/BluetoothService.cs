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
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<BluetoothService> _instance = new(() => new BluetoothService());
        public static BluetoothService Instance => _instance.Value;

        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;

        private const string SensorKey = "MainSensorId";

        public static readonly Guid SkiServiceId = Guid.Parse("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
        public static readonly Guid TelemetryCharacteristicId = Guid.Parse("beb5483e-36e1-4688-b7f5-ea07361b26a8");

        // FIX: la caratteristica viene cachata dopo il setup, così
        // DisconnectDevices può de-registrarla e RunCalibrationAsync
        // non deve ri-fare la scoperta GATT da zero.
        private ICharacteristic? _telemetryCharacteristic;

        public event EventHandler<SkiFrame>? DataReceived;

        // AGGIUNTA: permette alla UI di reagire ai cambi di stato BLE
        // senza dover aspettare il return di ConnectToSavedSensorAsync.
        // Usato da StartBackgroundConnection(). Non rompe nulla di esistente.
        public event EventHandler<BleConnectionState>? ConnectionStateChanged;

        public IDevice? ConnectedSensor { get; set; }

        public BluetoothState State() { return _ble.State; }

        private readonly TelemetryLogger _logger = new();

        // FIX: costruttore privato - era public, rendendo il singleton inutile.
        // new BluetoothService() fuori classe ora non compila più (voluto).
        private BluetoothService()
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;

            // FIX: riconnessione automatica su disconnect inatteso (era commentata).
            // Ora è sicura perché DisconnectDevices de-registra correttamente la characteristic.
            _adapter.DeviceDisconnected += async (s, e) =>
            {
                if (e.Device.Id != ConnectedSensor?.Id) return;
                ConnectedSensor = null;
                _telemetryCharacteristic = null;
                ConnectionStateChanged?.Invoke(this, BleConnectionState.Disconnected);
                await Task.Delay(2000);
                await ConnectToSavedSensorAsync();
            };
        }

        // ── API pubblica (invariata) ───────────────────────────────────────────

        public async Task StartScanning()
        {
            if (!_ble.IsOn) return;
            _adapter.ClearDeviceRegistries();
            await _adapter.StartScanningForDevicesAsync();
        }

        public void PairSensor(IDevice device)
        {
            Preferences.Default.Set(SensorKey, device.Id.ToString());
        }

        public async Task<bool> PairDevicesAsync()
        {
            Debug.WriteLine("[BLE] PairDevicesAsync");
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
                    // FIX: ConnectAndSetup non prende più tcs come parametro
                    // (era privato, nessun impatto esterno). tcs viene settato qui,
                    // unico posto, invece di avere due TrySetResult in gara.
                    bool ok = await ConnectAndSetup(e.Device);
                    tcs.TrySetResult(ok);
                }
            };

            _adapter.DeviceDiscovered += handler;
            using var cts = new CancellationTokenSource(7000);

            try
            {
                await _adapter.StartScanningForDevicesAsync(cts.Token);
                await Task.WhenAny(tcs.Task, Task.Delay(7000));
            }
            catch (Exception ex) { Debug.WriteLine($"[BLE] PairDevicesAsync error: {ex.Message}"); }
            finally
            {
                await _adapter.StopScanningForDevicesAsync();
                _adapter.DeviceDiscovered -= handler;
            }

            return tcs.Task.IsCompleted && tcs.Task.Result;
        }

        public async Task DisconnectDevices(bool dispose = true)
        {
            // FIX: de-registra la characteristic PRIMA di disconnettere il device.
            // In origine questo non avveniva mai, causando handler leak.
            if (_telemetryCharacteristic != null)
            {
                try
                {
                    _telemetryCharacteristic.ValueUpdated -= OnCharacteristicValueChanged;
                    if (ConnectedSensor?.State == DeviceState.Connected)
                        await _telemetryCharacteristic.StopUpdatesAsync();
                }
                catch (Exception ex) { Debug.WriteLine($"[BLE] Unsubscribe error: {ex.Message}"); }
                _telemetryCharacteristic = null;
            }

            if (ConnectedSensor == null) return;

            try { await _adapter.DisconnectDeviceAsync(ConnectedSensor); }
            catch { }
            finally
            {
                if (dispose) ConnectedSensor.Dispose();
                ConnectedSensor = null;
            }
        }

        public async Task<bool> ConnectToSavedSensorAsync()
        {
            Debug.WriteLine("[BLE] ConnectToSavedSensorAsync");
            var savedId = Preferences.Default.Get(SensorKey, string.Empty);
            if (string.IsNullOrEmpty(savedId)) return false;
            if (!Guid.TryParse(savedId, out var deviceId)) return false;

            try
            {
                if (_adapter.IsScanning)
                    await _adapter.StopScanningForDevicesAsync();

                await DisconnectDevices(dispose: false);
                await Task.Delay(300);

                using var cts = new CancellationTokenSource(8000);
                var device = await _adapter.ConnectToKnownDeviceAsync(deviceId, new ConnectParameters(), cts.Token);

                if (device == null)
                {
                    await DisconnectDevices(dispose: true);
                    Preferences.Default.Remove(SensorKey);
                    return false;
                }

                // FIX: il tcs che veniva passato qui non veniva mai awaited
                // (era creato solo per soddisfare la firma di ConnectAndSetup).
                // Ora ConnectAndSetup restituisce semplicemente bool.
                return await ConnectAndSetup(device);
            }
            catch (Exception ex)
            {
                await DisconnectDevices(dispose: true);
                Preferences.Default.Remove(SensorKey);
                Debug.WriteLine($"[BLE] ConnectToSavedSensor fallito: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RunCalibrationAsync(int samplesCount)
        {
            Debug.WriteLine("[BLE] RunCalibrationAsync");

            if (ConnectedSensor == null)
            {
                // FIX: era hardcoded "MainSensorId", ora usa la costante SensorKey
                var savedId = Preferences.Default.Get(SensorKey, string.Empty);
                bool connected = !string.IsNullOrEmpty(savedId)
                    ? await ConnectToSavedSensorAsync()
                    : await PairDevicesAsync();

                if (!connected) return false;
            }

            if (ConnectedSensor == null) return false;

            // FIX: usa _telemetryCharacteristic cachata invece di ri-fare
            // la scoperta GATT (GetServiceAsync + GetCharacteristicAsync).
            // Se per qualsiasi motivo è null, fallback al vecchio metodo.
            var characteristic = _telemetryCharacteristic;
            if (characteristic == null)
            {
                Debug.WriteLine("[BLE] RunCalibrationAsync: cache miss, fallback GATT discovery");
                var service = await ConnectedSensor.GetServiceAsync(SkiServiceId);
                if (service == null) return false;
                characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
                if (characteristic == null) return false;
            }

            int counter = 0;
            var tcs = new TaskCompletionSource<bool>();

            EventHandler<CharacteristicUpdatedEventArgs> handler = (s, e) =>
            {
                counter++;
                var frame = GetSkiFrameFromData(e.Characteristic.Value);

                bool isSettingsLoggingEnabled = true; // forcing logging
                if (!_logger.IsLogging && isSettingsLoggingEnabled)
                    _logger.StartLogging("Calibrations");
                else if (_logger.IsLogging)
                    _logger.LogData(frame);

                CalibrationService.Instance.AddFrame(frame, false);

                if (counter >= samplesCount) tcs.TrySetResult(true);
            };

            characteristic.ValueUpdated -= OnCharacteristicValueChanged;
            characteristic.ValueUpdated += handler;
            await characteristic.StartUpdatesAsync();

            var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));

            try
            {
                if (ConnectedSensor.State == DeviceState.Connected)
                    await characteristic.StopUpdatesAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"[BLE] RunCalibrationAsync StopUpdates error: {ex.Message}"); }

            characteristic.ValueUpdated -= handler;
            characteristic.ValueUpdated += OnCharacteristicValueChanged;

            if (_logger.IsLogging) _logger.StopLogging();

            if (result == tcs.Task)
            {
                Debug.WriteLine("[BLE] RunCalibrationAsync: Complete");
                CalibrationService.Instance.Save();
                await DisconnectDevices(dispose: false);
                return true;
            }

            Debug.WriteLine("[BLE] RunCalibrationAsync: Timeout");
            await DisconnectDevices();
            return false;
        }

        // AGGIUNTA: avvia la connessione in background senza bloccare la UI.
        // Chiama questo da OnAppearing/OnStart invece di awaiting ConnectToSavedSensorAsync.
        // Il risultato arriva tramite l'evento ConnectionStateChanged.
        //
        // PRIMA (causa skipped frames):
        //   await BluetoothService.Instance.ConnectToSavedSensorAsync();
        //
        // DOPO (non blocca la UI):
        //   BluetoothService.Instance.ConnectionStateChanged += OnBleStateChanged;
        //   BluetoothService.Instance.StartBackgroundConnection();
        public void StartBackgroundConnection()
        {
            ConnectionStateChanged?.Invoke(this, BleConnectionState.Connecting);
            _ = Task.Run(async () =>
            {
                bool ok = await ConnectToSavedSensorAsync();
                ConnectionStateChanged?.Invoke(this,
                    ok ? BleConnectionState.Connected : BleConnectionState.Failed);
            });
        }

        // ── Logica interna ─────────────────────────────────────────────────────

        // FIX: rimosso il parametro TaskCompletionSource<bool> tcs.
        // Era un antipattern: tcs veniva settato sia dentro questo metodo
        // che nel chiamante PairDevicesAsync, con due TrySetResult in gara.
        // Essendo private, nessun impatto sull'API pubblica.
        private async Task<bool> ConnectAndSetup(IDevice device)
        {
            await DisconnectDevices();
            await Task.Delay(300);
            try
            {
                if (device.State == DeviceState.Connected)
                {
                    try { await _adapter.DisconnectDeviceAsync(device); } catch { }
                    await Task.Delay(500);
                }

                await _adapter.ConnectToDeviceAsync(device);

                var mtu = await device.RequestMtuAsync(517);
                Debug.WriteLine($"[BLE] MTU negoziato: {mtu} byte");

                ConnectedSensor = device;
                Preferences.Default.Set(SensorKey, device.Id.ToString());

                await Task.Delay(600);

                var services = await device.GetServicesAsync();
                var service = services.FirstOrDefault(s => s.Id == SkiServiceId);
                if (service == null)
                {
                    Debug.WriteLine("[BLE] Servizio non trovato.");
                    await DisconnectDevices();
                    return false;
                }

                var characteristic = await service.GetCharacteristicAsync(TelemetryCharacteristicId);
                if (characteristic == null)
                {
                    Debug.WriteLine("[BLE] Caratteristica non trovata.");
                    await DisconnectDevices();
                    return false;
                }

                // FIX: salva la caratteristica nel field per riuso futuro.
                _telemetryCharacteristic = characteristic;

                characteristic.ValueUpdated -= OnCharacteristicValueChanged;
                characteristic.ValueUpdated += OnCharacteristicValueChanged;

                try { await characteristic.StopUpdatesAsync(); } catch { }
                await characteristic.StartUpdatesAsync();

                Debug.WriteLine("[BLE] Sensore pronto.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] ConnectAndSetup fallito: {ex.Message}");
                await DisconnectDevices();
                return false;
            }
        }

        private void OnCharacteristicValueChanged(object? s, CharacteristicUpdatedEventArgs e)
        {
            var data = e.Characteristic.Value;
            if (data == null || data.Length < 52)
            {
                Debug.WriteLine($"[BLE] Pacchetto incompleto ({data?.Length} bytes)");
                return;
            }
            DataReceived?.Invoke(this, GetSkiFrameFromData(data));
        }

        private static SkiFrame GetSkiFrameFromData(byte[] data)
        {
            uint ts = BitConverter.ToUInt32(data, 0);

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
    }

    // AGGIUNTA: enum di supporto per ConnectionStateChanged.
    // Non tocca nulla di esistente.
    public enum BleConnectionState { Connecting, Connected, Disconnected, Failed }
}