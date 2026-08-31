# TestFlight для КФУ ЭлЖур

Репозиторий полностью подготовлен к TestFlight без хранения секретов Apple в коде. Пока Apple Developer не активирован, workflow `iOS TestFlight` выполняет только бесплатную проверку публичной конфигурации. Обычный workflow `iOS` отдельно собирает и запускает приложение в iPhone Simulator.

## Бесплатная проверка

Workflow автоматически запускается для push и pull request, если меняется iOS-приложение или его конфигурация. Job `TestFlight configuration` проверяет:

- iOS target `net10.0-ios`;
- Bundle ID `io.github.l0naroff313.cfuschedule`;
- формат версии и базового номера сборки;
- декларацию export compliance;
- наличие и структуру Apple Privacy Manifest.

Ручной запуск с `upload_to_testflight=false` выполняет ту же проверку и не обращается к Apple, не требует сертификатов и ничего не загружает.

## Что потребуется после активации Apple Developer

До первого настоящего запуска необходимо создать в Apple Developer и App Store Connect:

1. Explicit App ID `io.github.l0naroff313.cfuschedule`.
2. Запись приложения «КФУ ЭлЖур» с тем же Bundle ID.
3. Сертификат `Apple Distribution`, экспортированный вместе с закрытым ключом в `.p12`.
4. Provisioning profile типа `App Store Connect`. Для TestFlight UDID iPhone в профиль не добавляется.
5. App Store Connect API key с правом загрузки сборок. Файл `AuthKey_XXXXXXXXXX.p8` скачивается только один раз.

Apple API key, сертификат, пароли и provisioning profile нельзя отправлять в чат или добавлять в Git.

## GitHub Actions Secrets

В `Settings → Secrets and variables → Actions` понадобятся следующие repository secrets:

| Secret | Значение |
| --- | --- |
| `IOS_DISTRIBUTION_CERTIFICATE_BASE64` | `.p12` в Base64 |
| `IOS_DISTRIBUTION_CERTIFICATE_PASSWORD` | пароль от `.p12` |
| `IOS_PROVISIONING_PROFILE_BASE64` | App Store Connect `.mobileprovision` в Base64 |
| `APP_STORE_CONNECT_API_KEY_ID` | Key ID из App Store Connect |
| `APP_STORE_CONNECT_ISSUER_ID` | Issuer ID из App Store Connect |
| `APP_STORE_CONNECT_API_PRIVATE_KEY_BASE64` | файл `.p8` в Base64 |

На Windows содержимое бинарного файла можно безопасно скопировать в буфер обмена такой командой:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\file")) | Set-Clipboard
```

Для синхронизации заметок и заданий также можно создать repository variable `UNIVERSITY_SCHEDULE_API_BASE_URL` с публичным HTTPS-адресом API. Если переменная отсутствует, приложение соберётся, но серверная синхронизация будет отключена.

## Первый запуск

1. Открыть `Actions → iOS TestFlight → Run workflow`.
2. Сначала оставить `upload_to_testflight=false` и убедиться, что preflight проходит.
3. После добавления всех Apple-данных запустить workflow с `upload_to_testflight=true`.
4. Workflow проверит тип provisioning profile, подпишет `.ipa`, проверит Bundle ID и уникальный build number, сохранит `.ipa` на 14 дней, выполнит валидацию Apple и загрузит сборку.
5. Дождаться обработки сборки в App Store Connect и добавить её во внутреннюю группу TestFlight.

Номер сборки берётся из `github.run_number`, поэтому увеличивается автоматически. Маркетинговая версия задаётся свойством `ApplicationDisplayVersion` в проекте.

Значение `ITSAppUsesNonExemptEncryption=false` означает, что текущая версия не использует неосвобождённое от декларации шифрование. Перед добавлением собственной криптографии, VPN, защищённых протоколов или иных криптографических функций это решение необходимо пересмотреть и повторно ответить на вопросы export compliance в App Store Connect.

## Официальная документация

- [Загрузка сборок в App Store Connect](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds)
- [App Store Connect API keys](https://developer.apple.com/help/app-store-connect/get-started/app-store-connect-api/)
- [Публикация iOS-приложения .NET MAUI из командной строки](https://learn.microsoft.com/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0)
- [Импорт сертификата и provisioning profile в GitHub Actions](https://docs.github.com/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications)
