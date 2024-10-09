/*
 * Arduino_Firmware.ino
 * Copyright (C) 2024 - Present, Julien Lecomte - All Rights Reserved
 * Licensed under the MIT License. See the accompanying LICENSE file for terms.
 */

#include <Servo.h>
#include <FlashStorage.h>

constexpr auto DEVICE_GUID = "55c7745e-d21a-43da-abc5-837adcb27344";

constexpr auto COMMAND_PING = "COMMAND:PING";
constexpr auto RESULT_PING = "RESULT:PING:OK:";

constexpr auto COMMAND_INFO = "COMMAND:INFO";
constexpr auto RESULT_INFO = "RESULT:DarkSkyGeek's Automated Telescope Cover And Spectral Calibrator Firmware v1.0";

constexpr auto COMMAND_COVER_OPEN = "COMMAND:COVER:OPEN";
constexpr auto RESULT_COVER_OPEN_OK = "RESULT:COVER:OPEN:OK";
constexpr auto RESULT_COVER_OPEN_NOK = "RESULT:COVER:OPEN:NOK";

constexpr auto COMMAND_COVER_CLOSE = "COMMAND:COVER:CLOSE";
constexpr auto RESULT_COVER_CLOSE_OK = "RESULT:COVER:CLOSE:OK";
constexpr auto RESULT_COVER_CLOSE_NOK = "RESULT:COVER:CLOSE:NOK";

constexpr auto COMMAND_COVER_CALIBRATE = "COMMAND:COVER:CALIBRATE";
constexpr auto RESULT_COVER_CALIBRATE_OK = "RESULT:COVER:CALIBRATE:OK";

constexpr auto COMMAND_COVER_GET_CALIBRATION = "COMMAND:COVER:GETCALIBRATION";
constexpr auto RESULT_COVER_GET_CALIBRATION = "RESULT:COVER:GETCALIBRATION:";

constexpr auto COMMAND_COVER_GETSTATE = "COMMAND:COVER:GETSTATE";
constexpr auto RESULT_COVER_STATE_OPENING = "RESULT:COVER:GETSTATE:OPENING";
constexpr auto RESULT_COVER_STATE_OPEN = "RESULT:COVER:GETSTATE:OPEN";
constexpr auto RESULT_COVER_STATE_CLOSING = "RESULT:COVER:GETSTATE:CLOSING";
constexpr auto RESULT_COVER_STATE_CLOSED = "RESULT:COVER:GETSTATE:CLOSED";

constexpr auto COMMAND_CALIBRATOR_ON = "COMMAND:CALIBRATOR:ON";
constexpr auto RESULT_CALIBRATOR_ON = "RESULT:CALIBRATOR:ON:OK";

constexpr auto COMMAND_CALIBRATOR_OFF = "COMMAND:CALIBRATOR:OFF";
constexpr auto RESULT_CALIBRATOR_OFF = "RESULT:CALIBRATOR:OFF:OK";

constexpr auto COMMAND_CALIBRATOR_GETSTATE = "COMMAND:CALIBRATOR:GETSTATE";
constexpr auto RESULT_CALIBRATOR_STATE_ON = "RESULT:CALIBRATOR:GETSTATE:ON";
constexpr auto RESULT_CALIBRATOR_STATE_OFF = "RESULT:CALIBRATOR:GETSTATE:OFF";

constexpr auto ERROR_INVALID_COMMAND = "ERROR:INVALID_COMMAND";

// Pins assignment. Change these depending on your exact wiring!
const unsigned int CALIBRATOR_SWITCH_PIN = 6;
const unsigned int SERVO_SWITCH_PIN = 7;
const unsigned int SERVO_FEEDBACK_PIN = 8;
const unsigned int SERVO_CONTROL_PIN = 9;

// Value used to determine whether the NVM (Non-Volatile Memory) was written,
// or we are just reading garbage...
const unsigned int NVM_MAGIC_NUMBER = 0x12345678;

enum CoverState {
  opening,
  open,
  closing,
  closed
} coverState;

enum CalibratorState {
  on,
  off
} calibratorState;

typedef struct {
  unsigned int magicNumber;
  double slope;
  double intercept;
} ServoCalibration;

FlashStorage(nvmStore, ServoCalibration);

Servo servo;
ServoCalibration servoCalibrationData;

// How long do we wait between each step in order to achieve the desired speed?
const unsigned long STEP_DELAY_MICROSEC = 30L * 1000; // 30 msec

// Variables used to move the servo in the main loop...
int servo_position;
unsigned long last_step_time;

void setup() {
  // Initialize serial port I/O.
  Serial.begin(57600);
  while (!Serial) {
    ;  // Wait for serial port to connect. Required for native USB!
  }
  Serial.flush();

  // Read servo calibration data oin Flash storage:
  servoCalibrationData = nvmStore.read();

  // Initialize pins...
  pinMode(CALIBRATOR_SWITCH_PIN, OUTPUT);
  pinMode(SERVO_SWITCH_PIN, OUTPUT);
  pinMode(SERVO_FEEDBACK_PIN, INPUT);
  pinMode(SERVO_CONTROL_PIN, OUTPUT);

  // Make sure the RX, TX, and built-in LEDs don't turn on, they are very bright!
  // Even though the board is inside an enclosure, the light can be seen shining
  // through the small opening for the USB connector! Unfortunately, it is not
  // possible to turn off the power LED (green) in code...
  pinMode(PIN_LED_TXL, INPUT);
  pinMode(PIN_LED_RXL, INPUT);
  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, HIGH);

  // Make sure the servo is initially de-energized...
  digitalWrite(SERVO_SWITCH_PIN, LOW);

  // Make sure the calibrator is initially turned off...
  digitalWrite(CALIBRATOR_SWITCH_PIN, LOW);
  calibratorState = off;

  servo_position = 0;
  last_step_time = 0L;

  // When there is no calibration data yet, we have to assume that the cover is closed...
  if (servoCalibrationData.magicNumber != NVM_MAGIC_NUMBER) {
    coverState = closed;
  } else {
    // Close the cover, in case it is not completely closed.
    // To make sure that `closeCover` does not have an undefined behavior,
    // we initialize the `coverState` variable to `open`, just in case.
    // That variable will be updated in the `closeCover` function,
    // and then again once the cover has completely closed.
    coverState = open;
    closeCover(false);
  }
}

void loop() {
  if (Serial.available() > 0) {
    String command = Serial.readStringUntil('\n');
    if (command == COMMAND_PING) {
      handlePing();
    } else if (command == COMMAND_INFO) {
      sendFirmwareInfo();
    } else if (command == COMMAND_COVER_GETSTATE) {
      sendCurrentCoverState();
    } else if (command == COMMAND_COVER_OPEN) {
      openCover(true);
    } else if (command == COMMAND_COVER_CLOSE) {
      closeCover(true);
    } else if (command == COMMAND_COVER_CALIBRATE) {
      calibrateCover();
    } else if (command == COMMAND_CALIBRATOR_GETSTATE) {
      sendCurrentCalibratorState();
    }else if (command == COMMAND_CALIBRATOR_ON) {
      turnCalibratorOn();
    } else if (command == COMMAND_CALIBRATOR_OFF) {
      turnCalibratorOff();
    } else if (command == COMMAND_COVER_GET_CALIBRATION) {
      sendCalibrationData();
    } else {
      handleInvalidCommand();
    }
  }

  // Blink the built-in LED to let the user know that the device needs to be calibrated once!
  // Note: The device needs to be recalibrated every time the firmware is flashed.
  if (servoCalibrationData.magicNumber != NVM_MAGIC_NUMBER) {
    digitalWrite(LED_BUILTIN, HIGH);
    delay(500);
    digitalWrite(LED_BUILTIN, LOW);
    delay(500);
  }

  if (coverState == opening || coverState == closing) {
    // Make sure we don't prematurely take a step if it's too early...
    unsigned long now = micros();
    if (now - last_step_time >= STEP_DELAY_MICROSEC) {
      last_step_time = now;

      if (coverState == opening) {
        servo_position++;
        if (servo_position >= 180) {
          servo_position = 180;
          coverState = open;
        }
      } else if (coverState == closing) {
        servo_position--;
        if (servo_position <= 0) {
          servo_position = 0;
          coverState = closed;
        }
      }

      servo.write(servo_position);

      if (coverState == open || coverState == closed) {
        powerDownServo();
      }
    }
  }
}

void handlePing() {
  Serial.print(RESULT_PING);
  Serial.println(DEVICE_GUID);
}

void sendFirmwareInfo() {
  Serial.println(RESULT_INFO);
}

void sendCurrentCoverState() {
  switch (coverState) {
    case opening:
      Serial.println(RESULT_COVER_STATE_OPENING);
      break;
    case open:
      Serial.println(RESULT_COVER_STATE_OPEN);
      break;
    case closing:
      Serial.println(RESULT_COVER_STATE_CLOSING);
      break;
    case closed:
      Serial.println(RESULT_COVER_STATE_CLOSED);
      break;
  }
}

void openCover(bool writeResultToSerial) {
  if (servoCalibrationData.magicNumber != NVM_MAGIC_NUMBER || (coverState != closed && coverState != closing)) {
    if (writeResultToSerial) {
      Serial.println(RESULT_COVER_OPEN_NOK);
    }
    return;
  }

  servo_position = powerUpServo();
  coverState = opening;

  if (writeResultToSerial) {
    Serial.println(RESULT_COVER_OPEN_OK);
  }
}

void closeCover(bool writeResultToSerial) {
  if (servoCalibrationData.magicNumber != NVM_MAGIC_NUMBER || (coverState != open && coverState != opening)) {
    if (writeResultToSerial) {
      Serial.println(RESULT_COVER_CLOSE_NOK);
    }
    return;
  }

  servo_position = powerUpServo();
  coverState = closing;

  if (writeResultToSerial) {
    Serial.println(RESULT_COVER_CLOSE_OK);
  }
}

void calibrateCover() {
  Serial.println(RESULT_COVER_CALIBRATE_OK);

  powerUpServo();

  int step = 10;
  int nDataPoints = 1 + 180 / step;

  double x[nDataPoints] = { 0 };
  double y[nDataPoints] = { 0 };

  for (int i = 0, pos = 0; pos <= 180; i++, pos = i * step) {
    servo.write(pos);
    delay(1000);
    int feedbackValue = analogRead(SERVO_FEEDBACK_PIN);
    x[i] = pos;
    y[i] = feedbackValue;
  }

  linearRegression(x, y, nDataPoints, &servoCalibrationData.slope, &servoCalibrationData.intercept);
  servoCalibrationData.magicNumber = NVM_MAGIC_NUMBER;
  nvmStore.write(servoCalibrationData);

  coverState = open;

  closeCover(false);
}

void sendCalibrationData() {
  Serial.print(RESULT_COVER_GET_CALIBRATION);
  if (servoCalibrationData.magicNumber != NVM_MAGIC_NUMBER) {
    Serial.println("0:0");
  } else {
    Serial.print(servoCalibrationData.slope);
    Serial.print(":");
    Serial.println(servoCalibrationData.intercept);
  }
}

void sendCurrentCalibratorState() {
  switch (calibratorState) {
    case on:
      Serial.println(RESULT_CALIBRATOR_STATE_ON);
      break;
    case off:
      Serial.println(RESULT_CALIBRATOR_STATE_OFF);
      break;
  }
}

void turnCalibratorOn() {
  digitalWrite(CALIBRATOR_SWITCH_PIN, HIGH);
  calibratorState = on;
  Serial.println(RESULT_CALIBRATOR_ON);
}

void turnCalibratorOff() {
  digitalWrite(CALIBRATOR_SWITCH_PIN, LOW);
  calibratorState = off;
  Serial.println(RESULT_CALIBRATOR_OFF);
}

void handleInvalidCommand() {
  Serial.println(ERROR_INVALID_COMMAND);
}

// Energize and attach servo.
int powerUpServo() {
  digitalWrite(SERVO_SWITCH_PIN, HIGH);

  // Default position (closed), which will be used only once,
  // before we have successfully calibrated the servo.
  int pos = 0;

  if (servoCalibrationData.magicNumber == NVM_MAGIC_NUMBER) {
    // Short delay, so that the servo has been fully initialized.
    // Not 100% sure this is necessary, but it won't hurt.
    delay(100);

    int feedbackValue = analogRead(SERVO_FEEDBACK_PIN);
    pos = (int)((feedbackValue - servoCalibrationData.intercept) / servoCalibrationData.slope);

    // Deal with slight errors in the calibration process...
    if (pos < 0) {
      pos = 0;
    } else if (pos > 180) {
      pos = 180;
    }
  }

  // This step is critical! Without it, the servo does not know its position when it is attached below,
  // and the first write command will make it jerk to that position, which is what we want to avoid...
  servo.write(pos);

  // The optional min and max pulse width parameters are actually quite important
  // and depend on the exact servo you are using. Without specifying them, you may
  // not be able to use the full range of motion (270 degrees for this project)
  servo.attach(SERVO_CONTROL_PIN, 500, 2500);

  return pos;
}

// Detach and de-energize servo to eliminate any possible sources of vibrations.
// Magnets will keep the cover in position, whether it is open or closed.
void powerDownServo() {
  servo.detach();
  digitalWrite(SERVO_SWITCH_PIN, LOW);
}

// Function to calculate the mean of an array.
double mean(double arr[], int n) {
    double sum = 0.0;
    for (int i = 0; i < n; i++) {
        sum += arr[i];
    }
    return sum / n;
}

// Function to calculate the slope and intercept of a linear regression line.
void linearRegression(double x[], double y[], int n, double *slope, double *intercept) {
    double x_mean = mean(x, n);
    double y_mean = mean(y, n);
    double numerator = 0.0;
    double denominator = 0.0;
    for (int i = 0; i < n; i++) {
        numerator += (x[i] - x_mean) * (y[i] - y_mean);
        denominator += (x[i] - x_mean) * (x[i] - x_mean);
    }
    *slope = numerator / denominator;
    *intercept = y_mean - (*slope * x_mean);
}
