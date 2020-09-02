param(
    [string] $SimplygonLicenseKey
)

.\SimplygonSDK_*\SetupSimplygon.ps1 -Quiet
.\SimplygonSDK_*\SimplygonLicenseApplication.exe -InstallLicense $SimplygonLicenseKey