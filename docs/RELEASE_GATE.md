# Release Gate (RC) AriaSignature

Перед выпуском нового `installer` каждый пункт должен быть выполнен успешно.

## Автоматические шаги

1. `dotnet build .\AriaSignature.slnx -c Release`
2. `dotnet test .\AriaSignature.slnx -c Release`
3. `dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -o .\publish\ui`
4. `dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -o .\publish\service`
5. `iscc .\installer\inno\AriaSignature.iss`

Рекомендуемый единый запуск:

- `powershell -ExecutionPolicy Bypass -File .\scripts\release-gate.ps1`

## Ручной приемочный чеклист

- Установщик полностью русскоязычный и запрашивает права администратора.
- После установки служба `AriaSignatureService` существует и запущена.
- UI запускается корректно, иконки отображаются в окне, трее и ярлыках.
- Закрытие окна сворачивает приложение в трей, пункт «Выход» завершает приложение.
- В интерфейсе отображаются диски и SMART-данные, полученные из service API.
- Создание/редактирование/включение/выключение/запуск/удаление задач архивации работает.
- Сценарии `File` и `MsSql` валидируются, ошибки понятны пользователю и попадают в логи.
- Удаление приложения корректно останавливает и удаляет службу.
