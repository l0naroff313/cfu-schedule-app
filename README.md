# КФУ ЭлЖур

Мобильное приложение с расписанием и личным электронным дневником для студентов КФУ им. В. И. Вернадского.

Проект разрабатывается на .NET 10: мобильный клиент использует .NET MAUI, SQLite и официальный JSON API КФУ, серверная часть — ASP.NET Core. Вход и регистрация в MVP отсутствуют: при первом запуске пользователь выбирает институт, направление, курс, группу и подгруппу.

## Основные разделы

- **Сегодня** — текущая и следующая пара, расписание дня и ближайшие задания.
- **Расписание** — расписание группы или преподавателя на день и неделю; текущая аудитория преподавателя берётся только из опубликованного занятия.
- **Задания** — домашние задания, дедлайны, материалы и связь с занятием.
- **Заметки** — отдельные сохранённые заметки, привязанные к предметам и занятиям.
- **Профиль** — учебные данные студента, статистика и настройки приложения.

## Дизайн

| Тёмная тема | Светлая тема |
| --- | --- |
| ![Тёмная тема](design-references/campus-pulse-five-tabs-dark.png) | ![Светлая тема](design-references/campus-pulse-five-tabs-light.png) |

## Скриншоты MVP

Скриншоты сняты на Android 16 после первого запуска для группы ПИ-б-о-252, 1 подгруппы.

| Первый запуск | Сегодня | Расписание |
| --- | --- | --- |
| <img src="docs/screenshots/mvp-first-run/01-first-launch.png" alt="Первый запуск" width="260"> | <img src="docs/screenshots/mvp-first-run/02-today.png" alt="Сегодня" width="260"> | <img src="docs/screenshots/mvp-first-run/03-schedule.png" alt="Расписание" width="260"> |

| Задания | Заметки | Профиль |
| --- | --- | --- |
| <img src="docs/screenshots/mvp-first-run/04-assignments.png" alt="Задания" width="260"> | <img src="docs/screenshots/mvp-first-run/05-notes.png" alt="Заметки" width="260"> | <img src="docs/screenshots/mvp-first-run/06-profile.png" alt="Профиль" width="260"> |

## Структура решения

```text
UniversitySchedule.sln
├── src
│   ├── UniversitySchedule.Domain
│   ├── UniversitySchedule.Application
│   ├── UniversitySchedule.Contracts
│   ├── UniversitySchedule.Infrastructure
│   ├── UniversitySchedule.Api
│   ├── UniversitySchedule.ScheduleImporter
│   ├── UniversitySchedule.Mobile.Core
│   └── UniversitySchedule.Mobile
├── tests
└── docs
```

Архитектурные решения описаны в `docs/architecture`, а исследование источника — в `docs/schedule-source.md`.

## Что уже реализовано

- каркас всех проектов и направленные project references;
- MAUI Shell с пятью согласованными вкладками;
- семантические ресурсы светлой и тёмной тем;
- первый запуск с реальным каталогом институтов, направлений, курсов и групп КФУ;
- загрузка расписания группы из официального API, разворачивание чётных/нечётных недель и фильтрация подгруппы;
- SQLite-кэш каталога, расписаний, поиска преподавателей и выбранного профиля с офлайн-fallback;
- текущая/следующая пара, расписание группы на день/неделю и точный поиск по 2345 преподавателям с текущей опубликованной аудиторией;
- полный справочный импорт 13 подразделений, 115 направлений, 423 корректных групп и 2049 обогащённых карточек преподавателей из Vuzopedia;
- заметки и домашние задания с привязкой к конкретной паре, локальным CRUD, дедлайнами, статусами, закреплением и SQLite-хранилищем;
- навигация расписания по дням и неделям стрелками, касанием даты или выбором произвольного дня в календаре, а также быстрые действия «добавить заметку/задание» из карточки пары;
- пять вкладок, выровненные с финальными светлой и тёмной дизайн-досками, включая карточки, фильтры, пустые состояния и профильную статистику;
- экран настроек с явными кнопками выхода, системной/светлой/тёмной темами и корректным оформлением системных панелей Android;
- анонимный установочный UUID и 256-битный секрет в Android Keystore/iOS Keychain без аппаратных или персональных идентификаторов;
- серверная регистрация установки: HMAC-хеш секрета в PostgreSQL, короткоживущий JWT и изоляция данных по проверенному `installation_id`;
- офлайн-очередь синхронизации создания, редактирования и удаления заметок/заданий с tombstones, серверным реестром идемпотентных мутаций, тремя повторами временных ошибок и сохраняемыми конфликтами;
- экран ручного разрешения конфликтов заметок и заданий с безопасным выбором локальной либо серверной версии;
- ASP.NET Core API каталога и расписания с точным поиском преподавателя, текущей/следующей парой и проверенным PostgreSQL fallback при недоступности КФУ;
- PostgreSQL-хранилище установок, личных данных, каталога, журнала публикаций и последних корректных документов официального API КФУ;
- версионированные API-контракты и health endpoint;
- unit-тесты доменной, прикладной и мобильной логики.

## Локальная сборка

Требуются .NET SDK 10 и workload .NET MAUI:

```powershell
dotnet workload install maui
dotnet restore UniversitySchedule.sln
dotnet build UniversitySchedule.sln --no-restore
dotnet test UniversitySchedule.sln --no-build --no-restore
```

Для Android также нужны Android SDK и JDK 21. Локальные пути можно указать в `Directory.Build.local.props`, скопировав `Directory.Build.local.props.example`; локальный файл исключён из Git.

API запускается командой:

```powershell
dotnet user-secrets --project src/UniversitySchedule.Api set "ConnectionStrings:PostgreSql" "Host=localhost;Port=5432;Database=cfu_schedule;Username=cfu_schedule;Password=YOUR_PASSWORD"
dotnet user-secrets --project src/UniversitySchedule.Api set "InstallationAuthentication:SecretPepper" "YOUR_RANDOM_SECRET_OF_AT_LEAST_32_BYTES"
dotnet user-secrets --project src/UniversitySchedule.Api set "InstallationAuthentication:JwtSigningKey" "ANOTHER_RANDOM_SECRET_OF_AT_LEAST_32_BYTES"
dotnet tool restore
dotnet ef database update --project src/UniversitySchedule.Infrastructure
dotnet run --project src/UniversitySchedule.Api
```

Основные публичные серверные маршруты:

```text
GET /api/v1/catalog/snapshot
GET /api/v1/catalog/institutes
GET /api/v1/catalog/institutes/{id}/directions
GET /api/v1/catalog/directions/{id}/groups
GET /api/v1/catalog/groups/search?query=...
GET /api/v1/catalog/teachers/search?query=...
GET /api/v1/catalog/teachers/{id}
GET /api/v1/schedule/groups/{id}
GET /api/v1/schedule/groups/{id}/current
GET /api/v1/schedule/teachers/{id}
GET /api/v1/schedule/teachers/{id}/current
GET /health/live
GET /health/ready
```

Ответы расписания содержат `X-Schedule-Source: cfu-live` либо `postgresql-cache`. Сервер сохраняет только проверенный JSON официального API и при ошибке источника возвращает последнюю рабочую копию. OpenAPI доступен в Development или при `OpenApi__Enabled=true` по адресу `/openapi/v1.json`.

Для локального запуска API и PostgreSQL через Docker скопируйте `.env.example` в `.env`, замените все секреты и выполните:

```powershell
docker compose up --build -d
docker compose ps
```

Контейнер применяет EF Core-миграции при старте. В production рекомендуется завершать TLS на Caddy/Nginx; мобильный клиент намеренно не отправляет установочный секрет по HTTP.

На Ubuntu те же значения задаются переменными окружения `ConnectionStrings__PostgreSql`, `InstallationAuthentication__SecretPepper` и `InstallationAuthentication__JwtSigningKey`. Секреты не входят в репозиторий.

Чтобы мобильная сборка отправляла очередь на сервер, укажите доступный с телефона или эмулятора HTTPS-адрес в локальном `Directory.Build.local.props`:

```xml
<UniversityScheduleApiBaseUrl>https://your-api.example/</UniversityScheduleApiBaseUrl>
```

Адрес без HTTPS намеренно отключает синхронизацию: установочный секрет никогда не отправляется по открытому HTTP. Локальная Ubuntu VM подходит для разработки, если устройство видит её в сети и доверяет TLS-сертификату. Без запущенного сервера приложение продолжает полноценно хранить изменения в SQLite и отправит их позже.

Справочник направлений и преподавателей обновляется командой:

```powershell
dotnet run --project src/UniversitySchedule.ScheduleImporter
```

После запуска PostgreSQL существующий проверенный справочник можно опубликовать без повторного обхода сайтов:

```powershell
$env:ConnectionStrings__PostgreSql="Host=localhost;Port=5432;Database=cfu_schedule;Username=cfu_schedule;Password=YOUR_PASSWORD"
dotnet run --project src/UniversitySchedule.ScheduleImporter -- --seed-postgres
```

Для полного обновления источников с одновременной записью файла и PostgreSQL используйте `--publish-postgres`.

Для просмотра расписания сервер по-прежнему не требуется: мобильный клиент читает официальный HTTPS API КФУ и использует SQLite-кэш. ASP.NET Core API и PostgreSQL нужны только для серверной копии и синхронизации личных заметок и заданий.

## Дальнейший план

1. Добавить загрузку серверного snapshot в локальную SQLite-базу для восстановления данных после переустановки на той же идентичности.
2. Автоматизировать регулярное обновление справочника и отчёта покрытия расписания.
3. Реализовать уведомления, провести расширенные accessibility/UI-тесты на Android и iOS и подготовить release-сборки.
