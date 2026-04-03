#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLE2902.h>
#include "esp_pm.h"

// --- CONFIGURAZIONI ---
uint8_t slaveAddress[] = {0x00, 0x4B, 0x12, 0xC4, 0xCB, 0xE4};
#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

SemaphoreHandle_t dataMutex;

// Struttura dati combinata (L dallo Slave via ESP-NOW, R dall'IMU interna)
struct __attribute__((packed)) SkiDataCombo {
    uint32_t timestamp; 
    float accXL, accYL, accZL;
    float gyroXL, gyroYL, gyroZL;
    float accXR, accYR, accZR;
    float gyroXR, gyroYR, gyroZR;
} dataCombo;

// Risorse FreeRTOS
BLECharacteristic *pCharacteristic;
volatile bool deviceConnected = false;

// --- CALLBACK ESP-NOW ---
void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    if (len == 24) {
        if (xSemaphoreTake(dataMutex, pdMS_TO_TICKS(5)) == pdTRUE) {
            memcpy(&dataCombo.accXL, incomingData, 24);
            xSemaphoreGive(dataMutex);
        }
    }
}

// --- CALLBACK BLE ---
class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
        deviceConnected = true;
        // Invia comando di START allo slave
        uint8_t cmd = 1; 
        esp_now_send(slaveAddress, &cmd, 1);
    }
    void onDisconnect(BLEServer* pServer) {
        deviceConnected = false;
        // Invia comando di STOP allo slave
        uint8_t cmd = 0;
        esp_now_send(slaveAddress, &cmd, 1);
        pServer->getAdvertising()->start();
    }
};

// --- TASK 1: DATA HARVESTER (IMU interna + BLE Notify) ---
void taskData(void *pvParameters) {
    TickType_t xLastWakeTime = xTaskGetTickCount();
    const TickType_t xFrequency = pdMS_TO_TICKS(20); // 100Hz

    for(;;) {
        if (deviceConnected) {
            dataCombo.timestamp = millis();
            
            // Leggi IMU Destra (Interna)
            M5.Imu.update();
            M5.Imu.getAccel(&dataCombo.accXR, &dataCombo.accYR, &dataCombo.accZR);
            M5.Imu.getGyro(&dataCombo.gyroXR, &dataCombo.gyroYR, &dataCombo.gyroZR);

            /*Serial.printf("[IMU] AccX:%.3f AccY:%.3f AccZ:%.3f | G_mag:%.3f\n",
            dataCombo.accXR, dataCombo.accYR, dataCombo.accZR,
            sqrt(pow(dataCombo.accXR,2) + pow(dataCombo.accYR,2) + pow(dataCombo.accZR,2)));
            Serial.printf("[IMU] ts:%.0f gyroXR:%.3f gyroYR:%.3f gyroZR:%.3f\n",
            dataCombo.timestamp, dataCombo.gyroXR, dataCombo.gyroYR, dataCombo.gyroZR);*/

            // Invia pacchetto completo allo smartphone
            if (xSemaphoreTake(dataMutex, pdMS_TO_TICKS(5)) == pdTRUE) {
                pCharacteristic->setValue((uint8_t*)&dataCombo, sizeof(dataCombo));
                pCharacteristic->notify();
                xSemaphoreGive(dataMutex);
            }
        }
        vTaskDelayUntil(&xLastWakeTime, xFrequency);
    }
}

// --- TASK 2: SYSTEM (Display, Batteria, Tasti) ---
void taskSystem(void *pvParameters) {
    int batteryLevel = 0;
    TickType_t lastBatteryCheck = 0;
    int lastDisplayedBattery = -1;
    bool lastDisplayedConn = false;
    bool displayActive = false;

    for(;;) {
        M5.update();

        if (M5.BtnA.wasPressed()) {
            displayActive = !displayActive;
            if (displayActive) {
                M5.Display.wakeup();
                M5.Display.setBrightness(10);
                M5.Display.fillScreen(BLACK);
                M5.Display.setCursor(10, 100);
                M5.Display.setTextSize(2);
                M5.Display.print("MASTER (R)");
                lastDisplayedBattery = -1; 
            } else {
                M5.Display.setBrightness(0);
                M5.Display.sleep();
            }
        }

        if (displayActive) {
            TickType_t currentTick = xTaskGetTickCount();
            if (currentTick - lastBatteryCheck > pdMS_TO_TICKS(10000)) {
                batteryLevel = M5.Power.getBatteryLevel();
                lastBatteryCheck = currentTick;
            }

            if (batteryLevel != lastDisplayedBattery || deviceConnected != lastDisplayedConn) {
                M5.Display.setTextSize(1);
                M5.Display.setCursor(10, 130);
                M5.Display.printf("BAT: %d%%    ", batteryLevel);
                M5.Display.setCursor(10, 145);
                M5.Display.printf("BLE: %s ", deviceConnected ? "CONN" : "DISC");
                
                lastDisplayedBattery = batteryLevel;
                lastDisplayedConn = deviceConnected;
            }
        }
        vTaskDelay(pdMS_TO_TICKS(100));
    }
}

void setup() {
    Serial.begin(115200);

    auto cfg = M5.config();
    M5.begin(cfg);
    WiFi.disconnect(true);
    WiFi.mode(WIFI_OFF); 
    delay(100);

    M5.Speaker.setVolume(255);
    M5.Speaker.tone(1000, 100); 
    delay(150);
    M5.Speaker.tone(1500, 100);
    delay(200);

    M5.Imu.begin();

    setCpuFrequencyMhz(160);
    // Risparmio Energetico
    /*esp_pm_config_esp32_t pm_config;
    pm_config.max_freq_mhz = 80; 
    pm_config.min_freq_mhz = 10;
    pm_config.light_sleep_enable = true;
    esp_pm_configure(&pm_config);*/
    dataMutex = xSemaphoreCreateMutex();
    // WiFi & ESP-NOW
    WiFi.mode(WIFI_STA);
    WiFi.setSleep(true);
    if (esp_now_init() == ESP_OK) {
        esp_now_register_recv_cb(esp_now_recv_cb_t(OnDataRecv));
        esp_now_peer_info_t peerInfo = {};
        memcpy(peerInfo.peer_addr, slaveAddress, 6);
        peerInfo.channel = 0;
        peerInfo.encrypt = false;
        esp_now_add_peer(&peerInfo);
    }

    // BLE Setup
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
    /*pServer->getAdvertising()->setMinPreferred(0x10); // 20ms (16 * 1.25ms)
    pServer->getAdvertising()->setMaxPreferred(0x20); // 40ms (32 * 1.25ms)*/
    pServer->getAdvertising()->start();

    M5.Display.setBrightness(0);
    M5.Display.sleep();
    
    

    // Task Creation
    xTaskCreatePinnedToCore(taskData, "DATA", 4096, NULL, 3, NULL, 1);
    xTaskCreatePinnedToCore(taskSystem, "SYS", 4096, NULL, 1, NULL, 1);
}

void loop() { vTaskDelete(NULL); }