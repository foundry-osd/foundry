# Getting started with Foundry OSD

## Before creating deployment media

Install the Windows ADK Deployment Tools and the matching Windows PE add-on on your workstation. Open Foundry OSD to check that these prerequisites are ready.

Foundry OSD downloads available updates at startup when startup checking is enabled. Once ready, choose **Apply update** in the navigation footer to install and restart. Closing normally also installs a ready update silently and leaves the application closed. While another Foundry OSD instance is running, closing does not install the update; it stays ready until no other instance is running.

If an update advisory appears before media creation or a USB update, apply the update first and start media creation again. You can also view the update, choose **Create anyway** with the current version, or cancel.

## Your first deployment

1. Configure your Windows deployment in Foundry OSD.
2. Create bootable ISO or USB media.
3. Boot a test device from the media and follow Foundry Connect and Foundry Deploy.

Creating USB media erases the selected USB device. Deploying Windows can erase or repartition the target disk. Test your configuration before production use.

If Foundry Connect or Foundry Deploy asks you to rebuild boot media, update Foundry OSD on the authoring workstation first, then recreate the ISO or update the USB media. This guidance also covers legacy media whose authoring version cannot be determined.

## Help and source code

- Quick start: https://docs.foundryosd.com/start-here/quick-start
- Project repository: https://github.com/foundry-osd/foundry
