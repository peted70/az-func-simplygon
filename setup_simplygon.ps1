param(
    [string] $SimplygonLicenseKey
)

.\Simplygon9\SetupSimplygon.ps1 -Quiet
.\Simplygon9\SimplygonLicenseApplication.exe -InstallLicense $SimplygonLicenseKey