# Quickstart: Connect an Orgpal PalThree to Azure IoT

In this quickstart you'll be connecting the Orgpal PalThree (from now on, PalThree) to Azure IoT.

![PalThree Board](./paltrhee-revi-board.jpg)

Sensor Protocols and Features On-Board

* RS 485/Modbus
* TTL Serial
* 4-20mA
* Flow Meters/Pulse Counts
* On Board Relay
* Ultra Low Power Modes
* RTC On Board
* USB File System
* SD Card/File System
* Flash File System on Board (16MB)
* Direct GPIO for analog and digital input in the 3.3/5V
* Expansion Slots for Modules and Add Ons
* Communication via Ethernet/Wifi, Cellular and Satellite

## What you need

* A [PalThree](https://www.orgpal.com/palthree-iot-azure)
* USB 2.0 A male to Micro USB male cable
* Ethernet cable RJ45 terminated
* Visual Studio 2022 (2017 should be OK to)
* [.NET nanoFramework VS extension](https://marketplace.visualstudio.com/items?itemName=nanoframework.nanoFramework-VS2022-Extension) installed in Visual Studio
* [nanoff tool](https://github.com/nanoframework/nanoFirmwareFlasher#install-net-nanoframework-firmware-flasher) installed
* Azure Account

## Clone the repo for the quickstart

Clone the following repo to download the sample code.

```shell
gh repo clone github.com/OrgPal/Azure-Certification.git
```

## Get the PalThree ready

The PalThree already has .NET nanoFramework firmware installed. You may want to check if it's running the latest version.

1. Install [nanoff tool](https://github.com/nanoframework/nanoFirmwareFlasher#install-net-nanoframework-firmware-flasher) if you haven't done so before.

```shell
dotnet tool install -g nanoff
```

2. Run the tool to update the firmware

```shell
nanoff --update --target ORGPAL_PALTHREE
```

## Open the solution in Visual Studio

Open the [PalThree-Azure-IoT solution](PalThree-Azure-IoT.sln) in Visual Studio.

## Store Azure Root CA certificate in the device

Uploading the Azure Root CA to the device make things easier (and simpler).
Please follow the [instructions](https://github.com/nanoframework/nanoFramework.Azure.Devices#certificate) on how to do this.

## Create the X.509 certificates for the device

If you need to create device a certificate for your device you have follow the [instructions](https://github.com/nanoframework/Samples/blob/main/samples/AzureSDK/AzureSDKSensorCertificate/create-certificate.md) here very closely.

## Setup Azure IoT Hub

Please find [here](https://github.com/nanoframework/Samples/tree/main/samples/AzureSDK/AzureSDKSensorCertificate#run-the-solution) the instructions on how to setup Azure IoT Hub and create an IoT device.
