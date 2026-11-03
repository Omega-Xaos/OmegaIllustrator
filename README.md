# Omega Illustrator

Omega Illustrator собирает редактируемые композиции из исходных SVG-объектов. В проект входят ручной редактор, смысловой подбор с проверкой ответа модели, три цветовые палитры и подключение AI-провайдеров.

## Запуск готовой Windows-сборки

Распакуйте пакет и запустите `Start-OmegaIllustrator.cmd`. Откроется локальный адрес `http://127.0.0.1:5190`. Пакет собран для Windows x64 и не требует установленного .NET SDK. Если браузер не открылся автоматически, вставьте этот адрес вручную.

## Сборка из исходного кода

Нужен .NET SDK 10. Проект приложения ссылается на библиотеку `Omega.AI.Gateway`, поэтому в архиве исходников оба каталога находятся рядом в `Source/`.

```powershell
dotnet restore OmegaIllustrator.slnx
dotnet build OmegaIllustrator.slnx -c Release
dotnet test OmegaIllustrator.slnx -c Release
dotnet run --project src/OmegaIllustrator.Web/OmegaIllustrator.Web.csproj --no-launch-profile -- --urls http://127.0.0.1:5190
```

Откройте `http://127.0.0.1:5190`. Для отдельной Windows-публикации:

```powershell
dotnet publish src/OmegaIllustrator.Web/OmegaIllustrator.Web.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## Работа без AI и подключение провайдера

Ручное добавление предметов и редактор работают без API-профиля. Для смыслового подбора откройте **Подключение ИИ** (`/ai`), добавьте профиль в API-разделе или подключите доступный официальный ChatGPT OAuth, затем проверьте профиль и выберите модель. Перечень моделей зависит от выбранного провайдера, его аккаунта, квот и доступности сети; бесплатная модель не гарантирует отсутствие ограничений.

Профили и секреты хранятся в пользовательском каталоге данных, отдельно от публикуемых статических файлов. По умолчанию это `%LOCALAPPDATA%\OmegaIllustrator`; для тестовой изолированной копии задайте переменную `OMEGA_DATA_DIR`. Не добавляйте ключи в исходники, архив или публичный сервер.

Подробное руководство по запуску готовой сборки, настройке API, проверке композиции, сборке без Visual Studio и размещению исходников в Git: [Настройка и передача](docs/SETUP_AND_DELIVERY.md).

## Документы

- [Архитектура](docs/ARCHITECTURE.md)
- [Каталог SVG и палитры](docs/ASSETS.md)
- [AI-провайдеры и инструменты](docs/AI_TOOLS.md)
- [Демонстрационные композиции](docs/DEMO.md)
- [Проверки](docs/TESTING.md)
- [Известные ограничения](docs/KNOWN_ISSUES.md)
- [Модель смысловой композиции и JSON-контракт](docs/COMPOSITION_MODEL.md)
- [Основное техническое задание](FINAL_TZ_Codex.md)
