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
- текущая/следующая пара, расписание группы на день/неделю и поиск преподавателя с текущей опубликованной аудиторией;
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
dotnet run --project src/UniversitySchedule.Api
```

Отдельный PostgreSQL или Ubuntu-сервер для текущего этапа не требуется: мобильный клиент читает официальный HTTPS API напрямую и работает с локальным SQLite-кэшем. Сервер понадобится на следующих этапах для синхронизации личных заметок между устройствами и резервного импорта.
