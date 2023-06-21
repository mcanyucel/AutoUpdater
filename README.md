# AutoUpdater

## Update Server Setup

The update server should respond to the following requests:
* 'GET ?app=APP_NAME' - returns the latest version of the app together with the download URL of the latest version, e.g.:
```
{
  "Version": "1.0.0",
  "Url": "http://example.com/app-1.0.0.zip"
}
```
Note that the download url should be a direct link to the zip file, not a page that contains the link.