#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>

// MAC ADDRESS del Master che abbiamo appena trovato
uint8_t masterAddress[] = {0x00, 0x4B, 0x12, 0xC0, 0xC7, 0x90};

bool masterWantsData = false;
bool displayOn = true; // Stato iniziale
static unsigned long lastBatUpdate = 0;

struct __attribute__((packed)) SlaveData {
    float accX, accY, accZ;
    float gyroX, gyroY, gyroZ;
} myData;

void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    if (len == sizeof(bool)) {
        masterWantsData = *(bool*)incomingData;
        if (masterWantsData) {
            M5.Display.fillCircle(5, 5, 3, GREEN); // Feedback: Sto inviando
        } else {
            M5.Display.fillCircle(5, 5, 3, RED);   // Feedback: In pausa
        }
    }
}



void setup() {
    auto cfg = M5.config();
    if (M5.BtnB.pressedFor(2000)) {
        M5.Display.fillScreen(RED);
        M5.Display.drawString("OFF", 40, 40);
        delay(1000);
        M5.Power.powerOff();
    }
    M5.begin(cfg);
    M5.Display.fillScreen(BLACK);
    M5.Display.drawString("SLAVE (L)", 10, 30);

    WiFi.mode(WIFI_STA);
    WiFi.disconnect();

    if (esp_now_init() == ESP_OK) {
        esp_now_register_recv_cb(esp_now_recv_cb_t(OnDataRecv));
    }

    esp_now_peer_info_t peerInfo = {};
    memcpy(peerInfo.peer_addr, masterAddress, 6);
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
    if (masterWantsData) {
        if (M5.Imu.update()) {
            M5.Imu.getAccel(&myData.accX, &myData.accY, &myData.accZ);
            M5.Imu.getGyro(&myData.gyroX, &myData.gyroY, &myData.gyroZ);
            esp_now_send(masterAddress, (uint8_t *) &myData, sizeof(myData));
        }
    }

    if (displayOn && (millis() - lastBatUpdate > 10000)) {
        int batPct = M5.Power.getBatteryLevel();
        
        // Puliamo solo l'area della scritta per non ridisegnare tutto lo schermo
        M5.Display.fillRect(10, 100, 120, 20, BLACK); 
        M5.Display.setCursor(10, 100);
        M5.Display.printf("BAT: %d%%", batPct);
        
        lastBatUpdate = millis();
    }

    delay(40); // 25Hz per stabilità
}