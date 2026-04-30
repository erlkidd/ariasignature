# AriaSignature Release Gate (RC)

Перед сборкой `installer` каждый пункт должен быть green.

## Автоматические шаги

1. `dotnet build .\AriaSignature.slnx -c Release`
2. `dotnet test .\AriaSignature.slnx -c Release`
3. `dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c Release -o .\publish\ui`
4. `dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c Release -o .\publish\service`
5. `iscc .\installer\inno\AriaSignature.iss`

## Ручной acceptance checklist

- Установщик русскоязычный, запрашивает права администратора.
- После установки служба `AriaSignatureService` существует и запущена.
- UI стартует, иконка корректная в окне/трее/ярлыке.
- Закрытие окна сворачивает в трей, пункт "Выход" завершает приложение.
- Видны диски и SMART-поля, данные приходят из service API.
- Создание/редактирование/удаление/запуск задач архивации работает.
- `File` и `MsSql` сценарии валидируются и логируются.
- Uninstall удаляет приложение и корректно убирает службу.
