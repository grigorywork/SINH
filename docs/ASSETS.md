# Источники ресурсов

Три утверждённых владельцем PNG:

https://github.com/grigorywork/NoteLS/tree/b1f4b1f8be850c5d63349cc63618b4a478cf3eff/app/src/main/res/drawable-nodpi

- launcher_glass.png → glass.png / glass.ico
- launcher_frosted.png → frosted.png / frosted.ico
- launcher_paper.png → paper.png / paper.ico

PNG сохранены без правок. ICO получены ресайзом в 256, 128, 64, 48, 32, 16 px с ImageMagick. Выбор значка не переписывает EXE. Права на эти ресурсы остаются у исходных правообладателей; перед публичным распространением требуется сохранить применимые условия исходника.

MaterialDesignInXamlToolkit: https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit, MIT. Версия NuGet 5.4.0. Лицензии зависимостей включаются в portable-пакет.

`design/sinh-mirror-concept.png`: эскиз собственного значка SINH, получен image_gen 2026-11-24 по референсу владельца и уточнению «элементы должны быть зеркальны». Прозрачный PNG. Сохранён как концепт, не заменяет утверждённые ресурсы и не является точным векторным мастер-файлом.

Десять шрифтов (regular/bold) взяты без изменений из того же закреплённого NoteLS-исходника `app/src/main/res/font`. Лицензии из `app/src/main/assets/font-licenses` сохранены рядом и включаются в portable-пакет. URL и SHA256 каждого файла записаны в `src/Sinh.App/Assets/Fonts/manifest.json`. Семейства встроены как WPF resources, FontFamily создаётся/кешируется только по выбору.

По последнему референсу владельца зеркальный голубой концепт включён как mirror.png/mirror.ico и выбран по умолчанию для нового профиля. PNG копируется без творческих изменений; ICO получен конвертацией Pillow в 16/24/32/48/64/128/256 px. Три первоначальных варианта сохранены.

Лицензии MaterialDesignThemes/Colors получены из закреплённого commit 14223ece627401f4e987fdd4ba2b1ac5674977ff, XamlBehaviors — eaf05bcb7395e3fbbb930f863180defc8e9928f7. Уведомления и лицензии .NET/WindowsDesktop взяты из runtime NuGet 10.0.15. Тексты в Assets/Licenses встроены в EXE и приложены к пакету.
