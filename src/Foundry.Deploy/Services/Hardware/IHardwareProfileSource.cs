namespace Foundry.Deploy.Services.Hardware;

internal interface IHardwareProfileSource
{
    HardwareProfileSnapshot Capture();
}
