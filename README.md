# Welcome 

This repository contains (part of) the outcome of creating a [Simplygon](simplygon.com/) cloud service using Azure Functions. In particular, this Function takes a zip file, extracts it, and runs Smplygon's polygon reduction alghorithms. After that, you'll get a zip file back with the results. We have tested this with Simplygon 9 (the paid version as of this writing).

Click the deploy to Azure button below to get up and running quickly. 

# Quick start

[Deploy to Azure Button]()

We'll ask for a valid Simplygon license key and a Simplygon SDK version in order to automatically set you up with those (i.e. download the SDK and install the license key file). 

## Slow start 

The C# code in this repository links to the Simplygon SDK which we can't include here (the same for their license). The above [deployment script](deploy.cmd) does the downloading and configuration of the SDK and licenses automatically, which are the following steps:

1. Download the latest SDK [zip file from Simplygon](https://www.simplygon.com/Downloads)
1. Extract it somewhere on the Azure Function's file system
1. Place your license key (`.dat` file) in the SDK's directory
1. Set up an environment variable (configuration variable) `SIMPLYGON_9_PATH` to point to the SDK location

The actual deployment can be done in several ways:

- Create a private git repository with all files (this code, SDK, license) included and use CI/CD to deploy it to Functions 
- Build everything locally and FTP it into the Function environment
- Use the [Functions CLI](https://github.com/Azure/azure-functions-core-tools) to publish everything and 
- Whatever is mentioned in our [docs](https://docs.microsoft.com/en-us/azure/azure-functions/functions-continuous-deployment)

When creating the Functions resource in Azure, please read the next section below to get the right size and configuration.

## Knowing your Azure resources

This Function does some fairly heavy work and requires appropriate hardware to finish in time. Azure Functions have a maximum runtime of 5 minutes, which is _not enough_ to remesh even a simple model using the regular consumption plan. For our experiments we used a EP2 plan (420 ACU, 7 GB memory) and managed to remesh the same model in about 45 seconds, but anything with a faster CPU will do it even quicker. Keeping that in mind, here are some more details of the Function:

- Azure Functions v2 with dotnet core runtime on Windows
- Switch the Functions runtime to **64 bits**, or Simplygon assembly won't be loaded
- EP1 plan or better (e.g. Premium App Service plans)

When the deployment is finished, you can run some code to fetch the results.

# Interacting with your new polygon reduction API

The Function exposes a `GET`/`POST` API with two parameters: `InputZipUri` and `OnScreenSize` (optional, defaults to 300). For a `POST` request, send a `JSON` object in the body; `GET` requests can use those parameters in the URL. 

- `InputZipUri` refers to a zip file (accessible for the Function of course) that contains valid polygon and material files in its root. Typically this would be a URL from something like an Azure Storage Account (as in the example below).
- `OnScreenSize` is the object's size in pixels and an input parameter for the remeshing. While the default is 300, a number larger than 20 is required with the actual value depending on your use case.

Here is an example call of the API using [curl](https://curl.haxx.se/download.html). The `InputZipUri` is a SAS token URI for an Azure Storage Account with public access and the returned content is a zip file to be stored (in this case) at `/tmp/out.zip`:  

```sh
$ curl -d '{"InputZipUri": "https://[BLOB ACCOUNT NAME].blob.core.windows.net/obj/[ZIPPED BLOB NAME WITH SAS TOKEN]" , "OnScreenSize": 300 }' https://[AZURE FUNCTION NAME].azurewebsites.net/api/Remesh -o /tmp/out.zip
 % Total    % Received % Xferd  Average Speed   Time    Time     Time  Current
                                 Dload  Upload   Total   Spent    Left  Speed
  0     0    0     0    0     0      0      0 --:--:-- --:--:-- --:--:--     0*   Trying 13.69.68.65...
* TCP_NODELAY set
* Connected to [AZURE FUNCTION NAME].azurewebsites.net (13.69.68.65) port 443 (#0)
* ALPN, offering h2
* ALPN, offering http/1.1
* successfully set certificate verify locations:
*   CAfile: /etc/ssl/cert.pem
  CApath: none
* TLSv1.2 (OUT), TLS handshake, Client hello (1):
} [236 bytes data]
* TLSv1.2 (IN), TLS handshake, Server hello (2):
{ [81 bytes data]
* TLSv1.2 (IN), TLS handshake, Certificate (11):
{ [3804 bytes data]
* TLSv1.2 (IN), TLS handshake, Server key exchange (12):
{ [333 bytes data]
* TLSv1.2 (IN), TLS handshake, Server finished (14):
{ [4 bytes data]
* TLSv1.2 (OUT), TLS handshake, Client key exchange (16):
} [70 bytes data]
* TLSv1.2 (OUT), TLS change cipher, Change cipher spec (1):
} [1 bytes data]
* TLSv1.2 (OUT), TLS handshake, Finished (20):
} [16 bytes data]
* TLSv1.2 (IN), TLS change cipher, Change cipher spec (1):
{ [1 bytes data]
* TLSv1.2 (IN), TLS handshake, Finished (20):
{ [16 bytes data]
* SSL connection using TLSv1.2 / ECDHE-RSA-AES256-GCM-SHA384
* ALPN, server did not agree to a protocol
* Server certificate:
*  subject: CN=*.azurewebsites.net
*  start date: Sep 24 02:18:56 2019 GMT
*  expire date: Sep 24 02:18:56 2021 GMT
*  subjectAltName: host "[AZURE FUNCTION NAME].azurewebsites.net" matched cert's "*.azurewebsites.net"
*  issuer: C=US; ST=Washington; L=Redmond; O=Microsoft Corporation; OU=Microsoft IT; CN=Microsoft IT TLS CA 5
*  SSL certificate verify ok.
> POST /api/Remesh HTTP/1.1
> Host: [AZURE FUNCTION NAME].azurewebsites.net
> User-Agent: curl/7.64.1
> Accept: */*
> Content-Length: 215
> Content-Type: application/x-www-form-urlencoded
>
} [215 bytes data]
* upload completely sent off: 215 out of 215 bytes
100   215    0     0  100   215      0      4  0:00:53  0:00:45  0:00:08     0< HTTP/1.1 200 OK
< Content-Length: 34704
< Content-Type: application/zip
< Set-Cookie: ARRAffinity=dcec2995d01b66c910bb7656957055c4bbea8f3f774649508c450938f74a3cfb;Path=/;HttpOnly;Domain=[AZURE FUNCTION NAME].azurewebsites.net
< Request-Context: appId=cid-v1:41475a2b-d72b-4a73-afdf-592764894c82
< Content-Disposition: attachment; filename=remeshed.zip; filename*=UTF-8''remeshed.zip
< Set-Cookie: ARRAffinity=d6026d0071737429e70c9737388a532ab57cee6f16877f29e9c8c1805a4a360a;Path=/;HttpOnly;Domain=[AZURE FUNCTION NAME].azurewebsites.net
< Date: Wed, 02 Sep 2020 12:05:11 GMT
<
{ [2253 bytes data]
100 34919  100 34704  100   215    748      4  0:00:53  0:00:46  0:00:07  9710
* Connection #0 to host [AZURE FUNCTION NAME].azurewebsites.net left intact
* Closing connection 0
```

# Contributing 

While this is a prototypical implementation, we welcome help in keeping this up to date and working. Feel free to add issues for what isn't working or send a PR to add specifc features that we didn't include. If you are looking for ideas, here is a list of things that we know are missing:

- Proper error status codes. Right now everything is a 500 if something goes wrong
- Improved robustness
- Testing with Simplygon 8 (free)

# Handcrafted ♥️ with by 

[Bart](/bart-jansen), [Carlos](/CarlosSardo), [Martin](/mtirion), [Peter](/peted70), [Claus](/celaus), [Jan](/jantielens)


# License

MIT