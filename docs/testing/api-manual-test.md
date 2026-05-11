# ThoughtBuffer.Api Manual Audio Upload Test

Start the API:

```powershell
dotnet run --project ThoughtBuffer.Api/ThoughtBuffer.Api.csproj --urls http://localhost:5087
```

Missing upload validation:

```powershell
curl.exe -i -X POST http://localhost:5087/api/ingestions/audio
```

Expected result: `400 Bad Request` with a JSON error.

Real upload test:

```powershell
curl.exe -F "file=@C:\path\to\test123.mp3" http://localhost:5087/api/ingestions/audio
```

Local test file available on this machine:

```powershell
curl.exe -F "file=@E:\REC_FILE\FOLDER01\260511_1606.mp3" http://localhost:5087/api/ingestions/audio
```

Default API output locations use `LocalStorage:RootPath`. If `RootPath` is blank, outputs appear under:

```text
%LOCALAPPDATA%\ThoughtBuffer\Api\Recordings
%LOCALAPPDATA%\ThoughtBuffer\Api\Transcripts
%LOCALAPPDATA%\ThoughtBuffer\Api\Notes
%LOCALAPPDATA%\ThoughtBuffer\Api\recordings.json
```

To use the shared local ThoughtBuffer folders instead:

```powershell
$env:LocalStorage__RootPath="$env:LOCALAPPDATA\ThoughtBuffer"
```

Then expected outputs are:

```text
%LOCALAPPDATA%\ThoughtBuffer\Recordings
%LOCALAPPDATA%\ThoughtBuffer\Transcripts
%LOCALAPPDATA%\ThoughtBuffer\Notes
%LOCALAPPDATA%\ThoughtBuffer\recordings.json
```

Manual end-to-end checklist:

1. Use a short "test 123" audio recording.
2. Confirm the API returns `200 OK`.
3. Confirm the uploaded file is stored in the recordings folder.
4. Confirm a transcript `.txt` file is created.
5. Confirm a markdown note is created.
6. Confirm `recordings.json` is updated.
7. If the OpenAI key is missing or invalid, confirm the API returns a clean JSON error without exception details.
