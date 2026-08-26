# Bundled converter runtime

This directory contains the converter code compiled directly into the UIS
Premium Redesign executable. It is synchronized from the workspace's shared
`UsfmContractCli` converter and removes the packaged application's former
runtime dependency on an adjacent CLI project and `dotnet run`.

When the shared converter changes, update this snapshot deliberately and run
the redesign regression suite. Do not replace these files during packaging or
copy generated CLI binaries into the repository.
