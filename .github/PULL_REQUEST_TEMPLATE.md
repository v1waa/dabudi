## Что изменено

Кратко опишите изменение и причину.

## Проверка

- [ ] `dotnet restore .\Source\dabudi\dabudi.csproj --locked-mode`
- [ ] `dotnet build .\Source\dabudi\dabudi.csproj -c Release --no-restore`
- [ ] `publish.cmd` создаёт `dist\dabudi.exe`
- [ ] Изменение вручную проверено на Windows x64
- [ ] В commit не добавлены `bin`, `obj`, `dist`, EXE, DLL или секреты

## Влияние на точность восстановления

Укажите, меняет ли PR поведение относительно восстановленной версии 2.5.8 и почему это необходимо.
