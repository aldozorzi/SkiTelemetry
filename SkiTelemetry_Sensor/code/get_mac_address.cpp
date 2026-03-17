#include <Arduino.h>
#include <WiFi.h>

void setup() {
  Serial.begin(115200);
  delay(1000); 

  // Il MAC address è legato all'interfaccia Wi-Fi
  WiFi.mode(WIFI_STA);
  
  Serial.println("\n--- IDENTIFICAZIONE HARDWARE ---");
  Serial.print("Il MAC ADDRESS e': ");
  Serial.println(WiFi.macAddress());
  Serial.println("---------------------------------");
}

void loop() {
  // Niente da fare qui
}