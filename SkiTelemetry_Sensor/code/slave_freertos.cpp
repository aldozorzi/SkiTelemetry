#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>
#include "esp_pm.h"

// --- CONFIGURAZIONI ---
uint8_t masterAddress[] = {0x00, 0x4B, 0x12, 0xC0, 0xC7, 0x90};

struct __attribute__((packed)) SlaveData {
    float accX, accY, accZ;
    float gyroX, gyroY, gyroZ;
};

QueueHandle_t imuQueue;
volatile bool masterWantsData = false;

// --- CALLBACK ESP-NOW ---
void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    if (len == 1) {
        if (incomingData[0] == 1) masterWantsData = true;
        else if (incomingData[0] == 0) masterWantsData = false;
    }
}

// --- TASK IMU (100Hz) ---
void taskIMU(void *pvParameters) {
    TickType_t xLastWakeTime = xTaskGetTickCount();
    const TickType_t xFrequency = pdMS_TO_TICKS(20); // 100Hz reali
    SlaveData data;
    
    for(;;) {
        if (masterWantsData) {
            M5.Imu.update();
            /*M5.Imu.getAccel(&data.accX, &data.accY, &data.accZ);
            M5.Imu.getGyro(&data.gyroX, &data.gyroY, &data.gyroZ);*/


            int16_t rawAccX = M5.Imu.getRawData(0);
            int16_t rawAccY = M5.Imu.getRawData(1);
            int16_t rawAccZ = M5.Imu.getRawData(2);
            int16_t rawGyroX = M5.Imu.getRawData(3);
            int16_t rawGyroY = M5.Imu.getRawData(4);
            int16_t rawGyroZ = M5.Imu.getRawData(5);

            // Conversione manuale basata su +-4G (8192 LSB/g) - per +-8G -> 4096.0f
            data.accX = (float)rawAccX / 8192.0f;
            data.accY = (float)rawAccY / 8192.0f;
            data.accZ = (float)rawAccZ / 8192.0f;
            data.gyroX = (float)rawGyroX / 16.4f;
            data.gyroY = (float)rawGyroY / 16.4f;
            data.gyroZ = (float)rawGyroZ / 16.4f;

            /*Serial.printf("[IMU] AccX:%.3f AccY:%.3f AccZ:%.3f | G_mag:%.3f\n",
            data.accX, data.accY, data.accZ,
            sqrt(pow(data.accX,2) + pow(data.accY,2) + pow(data.accZ,2)));
            Serial.printf("[IMU] gyroX:%.3f gyroY:%.3f gyroZ:%.3f\n",
            data.gyroX, data.gyroY, data.gyroZ);
            delay(5000);*/
            xQueueSend(imuQueue, &data, 0);
        }
        vTaskDelayUntil(&xLastWakeTime, xFrequency);
    }
}

// --- TASK COMUNICAZIONE ---
void taskCommunication(void *pvParameters) {
    SlaveData dataToSend;
    for(;;) {
        if (xQueueReceive(imuQueue, &dataToSend, portMAX_DELAY)) {
            esp_now_send(masterAddress, (uint8_t *) &dataToSend, sizeof(SlaveData));
        }
    }
}

// --- TASK SISTEMA ---
void taskSystem(void *pvParameters) {
    int batteryLevel = 0;
    TickType_t lastBatteryCheck = 0;
    int lastDisplayedBattery = -1;
    bool lastDisplayedMode = false;
    bool displayActive = false;

    for(;;) {
        M5.update();

        if (M5.BtnA.wasPressed()) {
            displayActive = !displayActive;
            if (displayActive) {
                M5.Display.wakeup();
                M5.Display.setBrightness(10); // Aumentata per visibilità
                // Riscriviamo la parte statica ogni volta che si sveglia per sicurezza
                M5.Display.fillScreen(BLACK);
                M5.Display.setCursor(10, 100);
                M5.Display.setTextSize(2);
                M5.Display.print("SLAVE (L)");
                lastDisplayedBattery = -1; // Forza refresh dati
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

            if (batteryLevel != lastDisplayedBattery || masterWantsData != lastDisplayedMode) {
                M5.Display.setCursor(10, 130);
                M5.Display.setTextSize(1);
                M5.Display.printf("BAT: %d%%    ", batteryLevel);
                M5.Display.setCursor(10, 145);
                M5.Display.printf("MODE: %s   ", masterWantsData ? "SENDING" : "IDLE");
                
                lastDisplayedBattery = batteryLevel;
                lastDisplayedMode = masterWantsData;
            }
        }
        vTaskDelay(pdMS_TO_TICKS(200)); // Più reattivo per il tasto
    }
}

void setup() {
    Serial.begin(115200);
    auto cfg = M5.config();
    M5.begin(cfg);
    M5.Speaker.setVolume(255);
    M5.Speaker.tone(1000, 100); 
    delay(150);
    M5.Speaker.tone(1500, 100);
    delay(200);

    // Inizializzazione IMU esplicita (fondamentale!)
    M5.Imu.begin();
    
    setCpuFrequencyMhz(160);
    // Risparmio Energetico
    /*esp_pm_config_esp32_t pm_config;https://www.skiforum.it/
    pm_config.max_freq_mhz = 80; 
    pm_config.min_freq_mhz = 10;
    pm_config.light_sleep_enable = true;
    esp_pm_configure(&pm_config);*/

    // Display setup iniziale
    M5.Display.setRotation(0); // Mette lo schermo in orizzontale
    M5.Display.setBrightness(0);
    M5.Display.sleep();

    // WiFi & ESP-NOW
    WiFi.mode(WIFI_STA);
    WiFi.setSleep(true);
    WiFi.setTxPower(WIFI_POWER_2dBm);
    
    if (esp_now_init() == ESP_OK) {
        esp_now_peer_info_t peerInfo = {};
        memcpy(peerInfo.peer_addr, masterAddress, 6);
        peerInfo.channel = 0;
        peerInfo.encrypt = false;
        esp_now_add_peer(&peerInfo);
        esp_now_register_recv_cb(OnDataRecv);
    }

    imuQueue = xQueueCreate(10, sizeof(SlaveData));

    // Dopo M5.Imu.begin(), usa il bus interno di M5Unified
    // M5StickC Plus 2: MPU6886 è su Wire1 (SDA=21, SCL=22 interni)
    Wire1.begin(21, 22);
    Wire1.beginTransmission(0x68);
    Wire1.write(0x1C);
    Wire1.write(0x08); // ±4g
    Wire1.endTransmission();

    // VERIFICA Scrittura registro
    /*delay(10);
    Wire1.beginTransmission(0x68);
    Wire1.write(0x1C);
    Wire1.endTransmission(false);
    Wire1.requestFrom(0x68, 1);
    if (Wire1.available()) {
        uint8_t accelCfg = Wire1.read();
        Serial.printf("[IMU] ACCEL_CONFIG: 0x%02X (atteso 0x08)\n", accelCfg);
    } else {
        Serial.println("[IMU] ERRORE: nessuna risposta su Wire1");
    }*/

    xTaskCreatePinnedToCore(taskIMU, "IMU", 4096, NULL, 3, NULL, 1);
    xTaskCreatePinnedToCore(taskCommunication, "COMM", 4096, NULL, 2, NULL, 1);
    xTaskCreatePinnedToCore(taskSystem, "SYS", 4096, NULL, 1, NULL, 1);
}

void loop() {
    vTaskDelete(NULL);
}