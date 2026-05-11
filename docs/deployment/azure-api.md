# ThoughtBuffer.Api Azure Deployment Notes

## Target

Use Azure App Service for the first hosted API deployment.

This phase still uses local filesystem storage. That is acceptable for early smoke testing, but it is temporary. App Service local storage should be replaced with Azure Blob Storage in the next storage phase before relying on hosted uploads for durable data.

## Required App Settings

Configure these in the App Service application settings:

```text
THOUGHT_BUFFER_OPENAI_KEY=<OpenAI API key>
```

or:

```text
OpenAI__ApiKey=<OpenAI API key>
```

Also configure:

```text
ASPNETCORE_ENVIRONMENT=Production
LocalStorage__RootPath=<temporary local storage path>
LocalStorage__MaxUploadBytes=26214400
```

For a basic App Service test, `LocalStorage__RootPath` can point to a writable local path. Treat anything stored there as temporary until Blob Storage is introduced.

## Local Run

```powershell
dotnet run --project ThoughtBuffer.Api/ThoughtBuffer.Api.csproj --urls http://localhost:5087
```

## Local Smoke Tests

Root endpoint:

```powershell
curl.exe http://localhost:5087/
```

Health endpoint:

```powershell
curl.exe http://localhost:5087/health
```

Non-secret config status:

```powershell
curl.exe http://localhost:5087/api/config/status
```

Missing upload validation:

```powershell
curl.exe -i -X POST http://localhost:5087/api/ingestions/audio
```

Real upload:

```powershell
curl.exe -F "file=@C:\path\to\test123.mp3" http://localhost:5087/api/ingestions/audio
```

## Deployed Smoke Tests

Replace `<app-url>` with the App Service URL:

```powershell
curl.exe https://<app-url>/
curl.exe https://<app-url>/health
curl.exe https://<app-url>/api/config/status
curl.exe -i -X POST https://<app-url>/api/ingestions/audio
curl.exe -F "file=@C:\path\to\test123.mp3" https://<app-url>/api/ingestions/audio
```

## Current Limitations

- Upload artifacts are stored on the local filesystem.
- No Azure Blob Storage yet.
- No queue or background worker yet.
- No authentication yet.
- No Twilio webhooks or streaming endpoints yet.
