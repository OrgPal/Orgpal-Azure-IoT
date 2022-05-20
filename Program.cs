// Copyright (c) Orgpal. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using nanoFramework.Azure.Devices.Client;
using nanoFramework.Azure.Devices.Provisioning.Client;
using nanoFramework.Azure.Devices.Provisioning.Client.PlugAndPlay;
using nanoFramework.Azure.Devices.Shared;
using nanoFramework.Networking;
using nanoFramework.Runtime.Native;
using System.Device.Gpio;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Device.Pwm;
using System.Device.Adc;
using System;

namespace Orgpal.PalThree.Azure
{
    public class Program
    {
        // module Twin properties
        const string PropertyManufacturer = "manufacturer";
        const string PropertyModel = "model";
        const string PropertyFwVersion = "fwVersion";

        // Azure IoT IDs
        // replace with the registration ID for your device
        const string RegistrationID = "nano-7C-9E-BD-F6-05-8C";

        // Replace the OneXX by the ID scope you'll find in your DPS
        public const string IdScope = "0neXXXXXXXX";
        // You can use as well your own address like yourdps.azure-devices-provisioning.net
        public const string DpsEndpoint = "global.azure-devices-provisioning.net";
        public const string ModelId = "dtmi:orgpal:palthree:palthree_demo_0;1";

        // ADC constants
        private const int AnalogReference = 3300;
        private const int MaximumAdcValue = 4095;


        private static DeviceClient _deviceClient;
        private static GpioPin _led1;

        public static void Main()
        {
            Debug.WriteLine("Hello from Orgpal PALTHREE running .NET nanoFramework!");

            // setup GPIO to control LED
            GpioController gpioController = new GpioController();
            _led1 = gpioController.OpenPin(PalThreePins.GpioPin.Led1, PinMode.Output);

            // turn LED off
            _led1.Write(PinValue.Low);

            // setup handler to handler reboot event
            Power.OnRebootEvent += Power_OnRebootEvent;

            // Valid date and time are required because we're using TLS
            var success = NetworkHelper.SetupAndConnectNetwork(requiresDateTime: true, token: new CancellationTokenSource(10_000).Token);

            if (success)
            {
                // network connected, carry on!

                // create DPS service
                var provisioningClient = ProvisioningDeviceClient.Create(DpsEndpoint, IdScope, RegistrationID, new X509Certificate2(_deviceCert, _deviceCertKey, null), null);

                // setup payload to pass model ID to registration service
                var pnpPayload = new ProvisioningRegistrationAdditionalData
                {
                    JsonData = PnpConvention.CreateDpsPayload(ModelId),
                };

                // register with DPS
                var myDevice = provisioningClient.Register(pnpPayload, new CancellationTokenSource(60000).Token);

                if (myDevice.Status != ProvisioningRegistrationStatusType.Assigned)
                {
                    Debug.WriteLine($"Registration is not assigned: {myDevice.Status}, error message: {myDevice.ErrorMessage}");
                    return;
                }

                // setup device client
                _deviceClient = new DeviceClient(
                    myDevice.AssignedHub,
                    myDevice.DeviceId,
                    new X509Certificate2(_deviceCert, _deviceCertKey, null),
                    modelId: ModelId);

                // set QoS
                _deviceClient.QosLevel = nanoFramework.M2Mqtt.Messages.MqttQoSLevel.AtLeastOnce;

                // setup callbacks for methods
                _deviceClient.AddMethodCallback(playSound);
                _deviceClient.AddMethodCallback(blinkLed);
                _deviceClient.AddMethodCallback(reboot);

                // add handler for status update changes
                _deviceClient.StatusUpdated += DeviceClient_StatusUpdated;

                // open device to connect to IoT
                if (!_deviceClient.Open())
                {
                    Debug.WriteLine("ERROR: Not connected to Azure IoT");
                    return;
                }

                // loop forever and send telemetry every 10 seconds
                while (true)
                {
                    SendTelemetry();

                    Thread.Sleep(10_000);
                }
            }
            else
            {
                // failed to connect to network
                Debug.WriteLine($"Can't connect to network: {NetworkHelper.Status}");

                if (NetworkHelper.HelperException != null)
                {
                    Debug.WriteLine($"Exception from NetworkHelper: {NetworkHelper.HelperException}");
                }
            }
        }

        private static void Power_OnRebootEvent()
        {
            // gracefully disconnect from Azure
            if (_deviceClient != null && _deviceClient.IsConnected)
            {
                _deviceClient.Close();
            }
        }

        private static void DeviceClient_StatusUpdated(object sender, StatusUpdatedEventArgs e)
        {
            Debug.WriteLine($"STATUS: {e.IoTHubStatus.Status} _ {e.IoTHubStatus.Message}");

            if (e.IoTHubStatus.Status == Status.Connected)
            {
                // on connected report the Twin properties
                ReportProperties();
            }
        }

        private static void ReportProperties()
        {
            TwinCollection twin = new();

            twin.Add(PropertyManufacturer, "Orgpal");
            twin.Add(PropertyModel, SystemInfo.Model);
            twin.Add(PropertyFwVersion, SystemInfo.Version);

            _deviceClient.UpdateReportedProperties(twin, new CancellationTokenSource(5_000).Token);
        }

        private static void SendTelemetry()
        {
            // read temperature
            double temperature = GetTemperatureOnBoard();

            // send telemetry packet
            _deviceClient.SendMessage($"{{\"temperature\":{temperature}}}", new CancellationTokenSource(5000).Token);

            // read supply voltage
            double suplyVoltage = GetSupplyVoltage();

            // send telemetry packet
            _deviceClient.SendMessage($"{{\"suplyVoltage\":{suplyVoltage}}}", new CancellationTokenSource(5000).Token);
        }

        /// <summary>
        /// Reads the supply voltage usually in 12V range.
        /// </summary>
        /// <param name="samplesToTake">Number of samples to read to average</param>
        /// <returns>Voltage value.</returns>
        private static double GetSupplyVoltage(byte samplesToTake = 5)
        {
            AdcChannel adcVBAT = new AdcController().OpenChannel(PalThreePins.AdcChannel.ADC1_IN8_VBAT);

            if (samplesToTake < 1)
            {
                samplesToTake = 5;
            }

            int average = 0;
            for (byte i = 0; i < samplesToTake; i++)
            {
                average += adcVBAT.ReadValue();
                Thread.Sleep(50);
            }

            average /= samplesToTake;

            // VBat = 0.25 x VIN adc count
            // voltage = ((3300 * average) / 4095)* 4;

            var voltage = (AnalogReference * average / MaximumAdcValue) * 0.004f;

            return voltage;
        }

        /// <summary>
        /// Reads the on board temperature sensor value.
        /// </summary>
        /// <param name="samplesToTake">Number of samples to read to average</param>
        /// <returns>Temperature value in C degrees.</returns>
        private static double GetTemperatureOnBoard(byte samplesToTake = 5)
        {
            AdcChannel adcTemp = new AdcController().OpenChannel(PalThreePins.AdcChannel.ADC1_IN13_TEMP);

            if (samplesToTake < 1)
            {
                samplesToTake = 5;
            }

            int average = 0;
            for (byte i = 0; i < samplesToTake; i++)
            {
                average += adcTemp.ReadValue();
                Thread.Sleep(50);
            }

            average /= samplesToTake;

            double miliVoltValue = (AnalogReference * average) / MaximumAdcValue;
            //Convert reading to temperature; some calculations are converted to constants to improve performance
            double temp = ((13.582 - Math.Sqrt(184.470724 + (0.01732 * (2230.8 - miliVoltValue)))) / (-0.00866)) + 30;

            return temp;
        }

        #region Device command handlers

        private static string playSound(int rid, string payload)
        {
            Debug.WriteLine($"playSound rid={rid}");

            // start a thread to play a sound on the buzzer
            new Thread(() =>
            {
                var freq = 2000;
                var delta = 250;
                var lengthInSeconds = 3;

                var buzzer = PwmChannel.CreateFromPin(PalThreePins.GpioPin.PWM_SPEAKER_PH12);

                buzzer.DutyCycle = 0.5;

                for (short i = 0; i < lengthInSeconds; i++)
                {
                    buzzer.Frequency = freq;

                    buzzer.Start();
                    Thread.Sleep(500);
                    buzzer.Stop();

                    freq += delta;

                    buzzer.Frequency = freq;
                    buzzer.Start();
                    Thread.Sleep(500);
                    buzzer.Stop();

                    if (freq < 1000 || freq > 3000)
                        delta *= -1;
                }


                buzzer.Stop();
                buzzer.Dispose();
                buzzer = null;
            }
            ).Start();

            return "";
        }

        private static string blinkLed(int rid, string payload)
        {
            Debug.WriteLine($"blinkLed rid={rid}");

            // start a thread to blink the LED a couple of times
            new Thread(() =>
            {
                // toggle LED
                _led1.Toggle();
                Thread.Sleep(500);

                _led1.Toggle();
                Thread.Sleep(500);

                _led1.Toggle();
                Thread.Sleep(500);

                _led1.Toggle();
            }
            ).Start();

            return "";
        }

        private static string reboot(int rid, string payload)
        {
            Debug.WriteLine($"Request to reboot {rid}");

            // start a thread to reboot in 2 secs
            new Thread(() =>
            {
                // wait for 2 seconds to allow reporting back to Azure
                Thread.Sleep(2_000);

                // reboot device
                Power.RebootDevice(2_000);
            }
            ).Start();

            return "";
        }

        #endregion

        // device certificate in PEM format
        private const string _deviceCert =
       @"-----BEGIN CERTIFICATE-----
MIIDADCCAeigAwIBAgIBATANBgkqhkiG9w0BAQsFADAVMRMwEQYDVQQDDApNeSBS
b290IENBMB4XDTIyMDUxNzE3MDU0NVoXDTIyMDcxODE3MDU0NVowEzERMA8GA1UE
AwwIQ2VydDg4OTkwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCj6dUu
Brxc3EES2VCxdIdhCSP359hv3lmNWcG6oj6oonXKwP7yl3t1WglAqO92uMuFXS/M
fbuKtTP2hWMJ9wAl3BbfSNVjKBwEFPMR8935+9qfliL6DIt+eWIVjsIDfS6MosWj
iEprn3vWu0O4j8Ub/frCmZ/JQH+NGsmp4/zw4pL7fV4uPuIagGa66bRBQcbURCzp
Hl//3vDpx5crgRNvCPqnhOxMUf/hmUECJC1y6RQ62MQclfrahUYgBkqbfP43BzWn
syP4P+IWrl/ck7nd/LXdAQMPrhaOz015WO5h0IH98gSuXIScA0hs+vqeUu/11TPw
uUw0r2Sp0eFcK9TrAgMBAAGjXTBbMAkGA1UdEwQCMAAwHQYDVR0OBBYEFKyteCZA
u3S4LvUc4gXBFTUMlzliMB8GA1UdIwQYMBaAFNMd3yVuaKFWjHgz7lxyaGmYcADW
MA4GA1UdDwEB/wQEAwIF4DANBgkqhkiG9w0BAQsFAAOCAQEARVl8HxV3Sp/57Q4v
r0Aj2tgo7VlpbKyG1y5SZemaAbFK4sO3OpvqPfJOkhY+YKjmoELm5dYEgN5S2ZlQ
8rCrK9A5N9s1Qc6SAni9X1Q8azlogLkafbetfm03xJK4VKDwQB30I5HeWroxwtXc
EV4AjhcxqPv3fTuq9DpxgYdr4kDoFGdyPIi8taoQ3kOt12KR+CUx+qCtwFTGDmoH
ZxmTL0EaICinQnktaI0X+tuisbSyGNWtzP7Mi6gJM0cm9k6AKetoAD/iNtDTbk+9
2hxuxmDePJ5RO5B+2Co6Xc9ZjNF4JNZ8SK90hAQbyJIOxtvtE1dIpfoxKHo30tep
y+LjmA==
-----END CERTIFICATE-----";

        // private key for device certificate in PEM format
        private const string _deviceCertKey =
       @"-----BEGIN RSA PRIVATE KEY-----
MIIEogIBAAKCAQEAo+nVLga8XNxBEtlQsXSHYQkj9+fYb95ZjVnBuqI+qKJ1ysD+
8pd7dVoJQKjvdrjLhV0vzH27irUz9oVjCfcAJdwW30jVYygcBBTzEfPd+fvan5Yi
+gyLfnliFY7CA30ujKLFo4hKa5971rtDuI/FG/36wpmfyUB/jRrJqeP88OKS+31e
Lj7iGoBmuum0QUHG1EQs6R5f/97w6ceXK4ETbwj6p4TsTFH/4ZlBAiQtcukUOtjE
HJX62oVGIAZKm3z+Nwc1p7Mj+D/iFq5f3JO53fy13QEDD64Wjs9NeVjuYdCB/fIE
rlyEnANIbPr6nlLv9dUz8LlMNK9kqdHhXCvU6wIDAQABAoIBAAH13tVal5p6uJKG
TodDerJdPsoNF/3FMzKpbf49SNzmeIcwxhErtk/MgGDUGcJvP7aRdewPCD+xSXT7
hRg46o5esq1VUda07kTZj/YD1ytRaPzQpGFzkKA2HnIIj5HQHPmUoXgtE0rJ5lwb
8u7m//daPcthtVNdtfN/lwWKUwZPLTFnzzVWLidUF70LhKQaTmGNbPc0Cz7inLLY
m2J5TgWrWcSfyfzUlY+w6JSuM8dziIUNzj4MrhuJ4jz8Z1/4x4R6DbhVVD4ukenO
fQi3gGALFrvv/oV7LctOJKbcfPT48Rr+LMgUJx89x6CqGcGX+PoeIHpAZD7x/DI5
lSQt7E0CgYEAzoUb1vra1MSFHk0ga+LDNjZDqBbEUhgb6Z94+QDCsi2DpmmDwNzg
jmA2NXDlfHAIscodm/xuwShQ9oAvSGuAme2Vk2wyVaR5Ct3PeL/i0H09GaXLYcAp
9Jg6UTo3y4csve3E6N799O9fWtddsgfcWPo4iiTlH6NxPVEpfGdyAG8CgYEAyy90
G23xay0Te+C4WPx6KT1yK3RXnxoovK0A+Zatc9N/hnAeeQStB2EeJxThkbdiYXyL
s/UtG3f2qBERu6619WCAGmdcQZ0M+WRlIg0Ark2Zhm8Jt0FaPOmzpTTLvDSmrgn8
9FFEzZmDCehxHA1hOpR7Nci248nw+beNjEe3OUUCgYAZVRyYH0dOiBioId/TPAqk
EL311W0ZgNmTq7skGCLJxml6tUCzHKTy8fxUS9fqjreST2+YXbucN/zOb+Tc5krt
FsadQc1e0gEDAzha4HbLCkG/bqXnBLJgzXeB1TlY3ujvF0ZrJkdSjzZMJ1TX7Lzr
sS3UTDhKzDqswdLr7qh+QwKBgHQRBnVIx5jepfuksgn7J9l5BUf3bLoxGkY1WZI5
1ZmNnpJwZ6ff2OuXOb8/eV9g720a1T8Wdg7z5024enXI5p5l4qeylYvRqACqre7W
mKX5JBMcSOOLDH4xTfK4hw1a0kAm6n2yEuiTobw+MKbCqeDpwrxFjNvwlpw/kzco
HG01AoGAc//IBHSZ2odlon+pE4DOVDdBq5QL5EWp+deQxyAZGYAx5ly5TrcfeUZK
qAqEADaRorzr2fVijO03ypKdFnwzyVnaDRb3sJyVYEH1vv/hG+aExa78STnu+Hwu
RpF0zForbqQcej4HcXsvlFTEsetvWfXIRkHtwJsr5dRZobTlG7s=
-----END RSA PRIVATE KEY-----";

    }
}
