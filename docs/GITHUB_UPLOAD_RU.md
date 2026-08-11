# Как загрузить dabudi на GitHub

Ниже два способа. В обоих случаях выполняйте команды из корня распакованного проекта — там, где лежат `README.md` и `dabudi.sln`.

## Перед началом

1. Создайте аккаунт GitHub.
2. Установите [Git for Windows](https://git-scm.com/download/win).
3. При первой работе с Git задайте имя и email:

   ```powershell
   git config --global user.name "Ваше имя"
   git config --global user.email "email@example.com"
   ```

4. Решите, будет репозиторий публичным или приватным. Перед публичной публикацией проверьте права на распространение кода и ресурсов и выберите лицензию.

## Способ 1 — через GitHub CLI

Установите [GitHub CLI](https://cli.github.com/), затем выполните:

```powershell
git init
git branch -M main
git add .
git commit -m "Initial commit: recovered dabudi 2.5.8 source"

gh auth login
gh repo create dabudi --public --source=. --remote=origin --push
```

Для приватного репозитория замените `--public` на `--private`.

## Способ 2 — создать репозиторий на сайте

1. Откройте <https://github.com/new>.
2. Укажите имя, например `dabudi`.
3. Выберите **Public** или **Private**.
4. Не включайте создание README, `.gitignore` и лицензии: эти файлы уже подготовлены или требуют вашего решения.
5. Нажмите **Create repository**.
6. Выполните команды, заменив `USERNAME` своим логином:

   ```powershell
   git init
   git branch -M main
   git add .
   git commit -m "Initial commit: recovered dabudi 2.5.8 source"
   git remote add origin https://github.com/USERNAME/dabudi.git
   git push -u origin main
   ```

При HTTPS-аутентификации используйте вход через браузер/Git Credential Manager или personal access token, а не пароль от аккаунта.

## Последующие обновления

```powershell
git add -A
git commit -m "Краткое описание изменения"
git push
```

## Выпуск EXE через GitHub Releases

Не добавляйте собранный `dabudi.exe` обычным commit. Создайте и отправьте тег:

```powershell
git tag v2.5.8
git push origin v2.5.8
```

GitHub Actions соберёт single-file EXE и создаст Release. Статус смотрите на вкладке **Actions**, готовый файл — на странице **Releases**.

## Если remote уже существует

Проверить адрес:

```powershell
git remote -v
```

Исправить его:

```powershell
git remote set-url origin https://github.com/USERNAME/dabudi.git
```

## Контроль перед публикацией

```powershell
git status
git ls-files | Select-String -Pattern '\.(exe|dll|pdb)$'
```

Вторая команда в норме ничего не выводит. В репозиторий должны попасть исходники и конфигурация, но не локальные результаты сборки.
