#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLE2902.h>

#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

BLECharacteristic *pCharacteristic;
bool deviceConnected = false;
uint8_t slaveAddress[] = {0x00, 0x4B, 0x12, 0xC4, 0xCB, 0xE4};
bool displayOn = true; // Stato iniziale
static unsigned long lastBatUpdate = 0;

struct __attribute__((packed)) SkiDataCombo {
    uint32_t timestamp; 
    float accXL, accYL, accZL;
    float gyroXL, gyroYL, gyroZL;
    float accXR, accYR, accZR;
    float gyroXR, gyroYR, gyroZR;
} dataCombo;

void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    if (len == 24) {
        memcpy(&dataCombo.accXL, incomingData, 24);
        M5.Display.fillCircle(5, 5, 3, GREEN); 
    }
}

void sendCommandToSlave(bool start) {
    esp_now_send(slaveAddress, (uint8_t *) &start, sizeof(start));
}

class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
        deviceConnected = true;
        M5.Display.fillCircle(15, 5, 3, BLUE); 
        sendCommandToSlave(true); // DICE ALLO SLAVE DI PARTIRE
    };
    void onDisconnect(BLEServer* pServer) {
    deviceConnected = false;
    M5.Display.fillCircle(15, 5, 3, RED);
    sendCommandToSlave(false); // DICE ALLO SLAVE DI FERMARSI
    M5.Display.fillCircle(5, 5, 3, RED); 
    // Lasciamo un attimo di respiro allo smartphone per accorgersi della chiusura
    delay(200); 
    pServer->getAdvertising()->start();
}
};

void setup() {
    auto cfg = M5.config();
    M5.begin(cfg);
    
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
}

void loop() {
    M5.update();
    if (M5.BtnA.wasPressed()) {
        displayOn = !displayOn;
        if (displayOn) {
            M5.Display.setBrightness(100); // Riaccendi (puoi mettere anche 60 per risparmiare)
            M5.Display.wakeup();
        } else {
            M5.Display.setBrightness(0);   // Spegni retroilluminazione
            M5.Display.sleep();            // Metti il display in basso consumo
        }
    }

    if (deviceConnected) {
        dataCombo.timestamp = millis();
        M5.Imu.update();
        M5.Imu.getAccel(&dataCombo.accXR, &dataCombo.accYR, &dataCombo.accZR);
        M5.Imu.getGyro(&dataCombo.gyroXR, &dataCombo.gyroYR, &dataCombo.gyroZR);

        pCharacteristic->setValue((uint8_t*)&dataCombo, sizeof(dataCombo));
        pCharacteristic->notify();
    }

    if (displayOn && (millis() - lastBatUpdate > 10000)) {
        int batPct = M5.Power.getBatteryLevel();
        
        // Puliamo solo l'area della scritta per non ridisegnare tutto lo schermo
        M5.Display.fillRect(10, 100, 120, 20, BLACK); 
        M5.Display.setCursor(10, 100);
        M5.Display.printf("BAT: %d%%", batPct);
        
        lastBatUpdate = millis();
    }
    
    delay(50); 
}