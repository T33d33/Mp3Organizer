# Recognition build — 2026-09-27

Application and complete dependency-free test executable compiled successfully with the .NET 10 SDK compiler, warnings treated as errors.

**183 passed, 0 failed.** See `test-results.txt`.

Build command: `./build-offline.ps1 -OutputDirectory artifacts-recognition`

Test command: `dotnet artifacts-recognition/Mp3Organizer.Tests.dll <scratch-directory>`

Runtime LLM calls, AcoustID and MusicBrainz were mocked. Test audio was generated under the development workspace. No real library was scanned, no real MP3 tags changed, and no live identification/inference request was made. API documentation was consulted. Standard NuGet/MSBuild restore and live model access remain unverified.

Start with `../RECOGNITION.md` for commands, API configuration, quota/resume semantics, database provenance and opt-in tag-writing safeguards. Use this directory's DLL explicitly; old build directories and launchers are unchanged.
