# dabudi

- This project belongs in https://github.com/v1waa/dabudi. The user directs all updates to main and ready Windows builds to GitHub Releases.
- Work on main. Preserve remote history; never force-push. Check the current remote before publishing.
- Use C#/.NET and WPF. Do not introduce Python or require an installed runtime for the distributed EXE.
- Keep domain logic in Dabudi.Core, OS integration in Dabudi.Infrastructure, and UI/application coordination in Dabudi.App.
- Keep user-facing text in Russian and retain the name dabudi.
- Validate meaningful behavior with the regression runner and run locked restore, Release build, and self-contained publish before tagging a release.
- Release assets must be built from the tagged commit and include SHA-256 checksums. Do not commit generated binaries.
