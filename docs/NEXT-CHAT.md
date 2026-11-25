# SINH — итог публикации

**0.4.0-preview.3 публично опубликован**: https://github.com/grigorywork/SINH/releases/tag/v0.4.0-preview.3
Прямой EXE: https://github.com/grigorywork/SINH/releases/download/v0.4.0-preview.3/SINH.exe
Репозиторий: https://github.com/grigorywork/SINH (public, main). Исходная история владельца сохранена merge, без force push.

## Что проверено

- Linux:58/58 проверок ядра; предыдущие часовые прогоны и отдельный safety stress в docs/validation. Они относятся к своим зафиксированным версиям/SHA, а не автоматически к последнему бинарнику.
- Windows Server2025/10.0.26100 x64: source run45690034185, commit6b07e48 — build0 warnings/errors, 61/61 engine, все WPF-группы PASS (иконки/темы/GC/масштаб/стекло/English/малое окно/cancel/font10/MSS9LN).
- Publisher run45691898021: **точный подписанный EXE** из ZIP запущен на Windows, окно SINH появилось и штатно закрылось с ExitCode0. SHA проверен; Authenticode повторно проверен osslsigncode с явным публичным certificate, system trust не менялся. Релиз опубликован только после smoke PASS.
- GitHub REST digest опубликованных EXE/ZIP/source соответствует локальному. Прямая публичная ссылка EXE проверена: HTTP200.

EXE SHA256: `a4ba6bc7283950410e94f6c8d29835b8445b8b76046e8b2c1f5ceaf24d0c2c0f`.
Подпись MSS9LN **самоподписанная / не доверенный издатель**. Windows Get-AuthenticodeSignature возвратил UnknownError для недоверенной цепочки; это не помечается как доверенная подпись. Закрытый ключ не передавался в Git/Actions/Releases. Публичный cert приложен.

## Что изменилось после локального preview1

Native CreateFileW получил extended DOS/UNC prefix, восстановление длинных Unicode путей теперь PASS на Windows. WPF readonly ProgressBar.Value явно OneWay; общий Window style применяется к производным окнам, иконки/шрифты загружаются по SINH;component URI. Несовместимый Merriweather заменён на оригинальные static2.002 upstream Google Fonts b848f42f... с OFL; остальные9 семейств не изменены. Бинарникpreview2 с дефектом только дополнительного шрифта был выдан отдельно, актуаленpreview3.

## Публикация

Cloud CLI upload-host возвращал401, поэтому подписанные архивы переданы через временную release-assets ветку, а поддерживаемый GitHub Actions workflow проверил хеши/источник/первый запуск и опубликовал EXE/ZIP/source/bundle/сертификат/JSON/SHA. Старый пустой draft1 удалён; временная ветка удалена после успешной публикации. Main содержит исходник, без больших бинарников. Локальные пакеты в artifacts/release; пользовательские прежние download-файлы не удалены.

## Следующий этап

Клиентские **Windows10/11**, реальные USB/SMB, стандартный (не runneradmin) пользователь, DPI100/125/150/200/несколько мониторов, сенсор/Narrator требуют проверки по docs/WINDOWS-VALIDATION.md. Windows Server не подменяет эти результаты. Linux GUI — после Windows. Никаких покупок/платныхИИ; не менять identity или доверенные хранилища. Рефакторинг модульный, C#/WPF/Material Design. MSS9LN: каждое2-е нажатие прыжок, каждое9-е пасхалка; reduced-motion соблюдается.

Последняя проблема пользователя была **скачивание по локальной /workspace-ссылке**, а не ошибка EXE. Решена выдачей обычного публичного GitHub download URL. Если он сообщит новую ошибку, сначала различить скачивание/запуск/синхронизацию; Windows-проверки и фактический запуск уже есть.
