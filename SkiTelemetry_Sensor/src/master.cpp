#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLE2902.h>
#include <driver/rtc_io.h> 

// Dizionario dei comandi per la comunicazione Master-Slave
enum Command {
    CMD_STOP  = 0,  // Pausa invio dati
    CMD_START = 1,  // Avvio invio dati
    CMD_SLEEP = 2   // Vai in Light Sleep
};

#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

BLECharacteristic *pCharacteristic;
bool deviceConnected = false;
uint8_t slaveAddress[] = {0x00, 0x4B, 0x12, 0xC4, 0xCB, 0xE4};
bool displayOn = false; // Stato iniziale
static unsigned long lastBatUpdate = 0;

static float lastCheckX = 0;
static int stallCount = 0;

struct __attribute__((packed)) SkiDataCombo {
    uint32_t timestamp; 
    float accXL, accYL, accZL;
    float gyroXL, gyroYL, gyroZL;
    float accXR, accYR, accZR;
    float gyroXR, gyroYR, gyroZR;
} dataCombo;

void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    if (len == 24) {
        // Copia e basta. Non chiamare display, non chiamare BLE.
        memcpy(&dataCombo.accXL, incomingData, 24);
    }
}

void sendCommandToSlave(Command cmd) {
    uint8_t val = (uint8_t)cmd;
    esp_now_send(slaveAddress, &val, 1);
}

class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
        deviceConnected = true;
        M5.Display.fillCircle(5, 5, 3, BLUE); 
        sendCommandToSlave(CMD_START); // DICE ALLO SLAVE DI PARTIRE
    };
    void onDisconnect(BLEServer* pServer) {
    deviceConnected = false;
    M5.Display.fillCircle(5, 5, 3, RED);
    sendCommandToSlave(CMD_STOP); // DICE ALLO SLAVE DI FERMARSI
    // Lasciamo un attimo di respiro allo smartphone per accorgersi della chiusura
    delay(200); 
    pServer->getAdvertising()->start();
}
};

void goToLightSleep() {
    // 1. Feedback sonoro di spegnimento
    M5.Speaker.tone(1500, 100);
    delay(150);
    M5.Speaker.tone(1000, 100);

    // 2. Spegnimento visivo
    M5.Display.setBrightness(0);
    M5.Display.sleep();
    displayOn = false;

    // 3. Attesa rilascio tasto (se chiamato da pressione locale)
    while (M5.BtnA.isPressed()) {
        M5.update();
        delay(10);
    }
    delay(200); // Debounce finale

    // 4. Configura risveglio e dormi
    esp_sleep_enable_ext0_wakeup(GPIO_NUM_37, 0);
    esp_light_sleep_start();

    // --- IL CHIP SI FERMA QUI ---

    // 5. Logica di RISVEGLIO
    delay(100); // Pausa per stabilizzare l'hardware
    M5.Display.setBrightness(0);
    M5.Display.sleep();
    displayOn = true;

    // Feedback sonoro di risveglio
    M5.Speaker.tone(1000, 100);
    delay(150);
    M5.Speaker.tone(1500, 100);

    M5.Imu.begin(); // Forza il re-init del sensore hardware
    M5.Imu.setCalibration(0,0,0);
}

void setup() {
    Serial.begin(115200);

    auto cfg = M5.config();
    cfg.internal_imu = true;
    M5.begin(cfg);
    M5.Imu.setCalibration(0, 0, 0);

    M5.Display.setBrightness(0);
    M5.Display.sleep(); 
    // Inizializza lo speaker
    M5.Speaker.setVolume(255); // Volume da 0 a 255
    M5.Speaker.tone(1000, 100); // Beep di avvio (Frequenza, Durata)
    delay(150);
    M5.Speaker.tone(1500, 100);


    
    
    // Inizializziamo prima il Wi-Fi per ESP-NOW
    WiFi.mode(WIFI_STA);
    if (esp_now_init() == ESP_OK) {
        esp_now_register_recv_cb(esp_now_recv_cb_t(OnDataRecv));
    }
    // BLE Minimalista
    BLEDevice::init("MASTER");
    BLEServer *pServer = BLEDevice::createServer();
    pServer->setCallbacks(new MyServerCallbacks());
    BLEService *pService = pServer->createService(SERVICE_UUID);
    
    pCharacteristic = pService->createCharacteristic(
                        CHARACTERISTIC_UUID,
                        BLECharacteristic::PROPERTY_NOTIFY
                      );

    pCharacteristic->addDescriptor(new BLE2902());
    pService->start();
    
    pServer->getAdvertising()->addServiceUUID(SERVICE_UUID);
    pServer->getAdvertising()->start();

    M5.Display.fillScreen(BLACK);
    M5.Display.drawString("MASTER (R)", 10, 40);

    esp_now_peer_info_t peerInfo = {};
    memcpy(peerInfo.peer_addr, slaveAddress, 6);
    peerInfo.channel = 0;  
    peerInfo.encrypt = false;
    esp_now_add_peer(&peerInfo);

    M5.Imu.begin();
    M5.Imu.setCalibration(0, 0, 0);
}

void loop() {
    M5.update();
    if (M5.BtnA.wasPressed()) {
        displayOn = !displayOn;
        if (displayOn) {
            M5.Display.setBrightness(10); // Riaccendi (puoi mettere anche 60 per risparmiare)
            M5.Display.wakeup();
        } else {
            M5.Display.setBrightness(0);   // Spegni retroilluminazione
            M5.Display.sleep();            // Metti il display in basso consumo
        }
    }
    if (M5.BtnA.pressedFor(2000)) {
        M5.Speaker.tone(1500, 100);
        delay(150);
        M5.Speaker.tone(1000, 100);
        sendCommandToSlave(CMD_SLEEP);
        delay(100);
        goToLightSleep();
        sendCommandToSlave(CMD_STOP);
    }

    if (deviceConnected) {
        dataCombo.timestamp = millis();
        if (M5.Imu.update()) {
            M5.Imu.getAccel(&dataCombo.accXR, &dataCombo.accYR, &dataCombo.accZR);
            M5.Imu.getGyro(&dataCombo.gyroXR, &dataCombo.gyroYR, &dataCombo.gyroZR);
            // Se il valore è IDENTICO al precedente per 100 cicli, il sensore è piantato
            if (dataCombo.accXR == lastCheckX) {
                stallCount++;
            } else {
                stallCount = 0;
                lastCheckX = dataCombo.accXR;
            }

            if (stallCount > 10) {
                M5.Imu.begin(); // Forza il re-init del sensore hardware
                M5.Imu.setCalibration(0,0,0);
                stallCount = 0;
            }
            
            // Invia i dati
            pCharacteristic->setValue((uint8_t*)&dataCombo, sizeof(dataCombo));
            pCharacteristic->notify();
            vTaskDelay(1);
        }
        
    }

    if (displayOn){
        if(millis() - lastBatUpdate > 10000) {
            int batPct = M5.Power.getBatteryLevel();
            
            // Puliamo solo l'area della scritta per non ridisegnare tutto lo schermo
            M5.Display.fillRect(10, 100, 120, 20, BLACK); 
            M5.Display.setCursor(10, 100);
            M5.Display.printf("BAT: %d%%", batPct);
            
            lastBatUpdate = millis();
        }
    }
    
    delay(20); 
}