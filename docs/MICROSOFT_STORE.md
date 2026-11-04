# Microsoft Store release

Yacplayer remains open source under the MIT license when distributed through Microsoft Store. The package includes `LICENSE` and `THIRD_PARTY_NOTICES.md`; the Store listing should link to the public source repository.

## Build the package

Prerequisites: .NET 10 SDK and Windows 10/11 SDK with `MakeAppx.exe`.

Local unsigned test package with a non-production identity:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File packaging\msix\Build-Msix.ps1
```

The result is `artifacts/msix/Yacplayer-1.26.5.11-win-x64.msix`. It is self-contained. The default identity contains Microsoft's unsigned-package OID and is only for local validation.

For Store submission:

1. Reserve the product name in Partner Center.
2. Open **Product management → Product identity** and copy the exact `Package/Identity/Name`, `Package/Identity/Publisher`, and `Package/Properties/PublisherDisplayName` values.
3. Copy `packaging/msix/StoreIdentity.example.psd1` to `packaging/msix/StoreIdentity.local.psd1` and replace the samples. The local file is ignored by Git.
4. Choose a monotonically increasing four-part package version and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File packaging\msix\Build-Msix.ps1 `
  -UseLocalStoreIdentity -Version 1.26.5.11
```

Upload the resulting `.msix` on the submission's Packages page. Microsoft re-signs an accepted MSIX, so no PFX, certificate password, signing token, or private key belongs in this repository. A package distributed outside Microsoft Store must be signed separately.

## Manifest and certification notes

- Windows Desktop x64, minimum Windows 10 version 1809 (`10.0.17763.0`).
- `runFullTrust` is the only restricted capability. It is required for the existing Win32 console app; no network capability is declared.
- The package provides a Start menu entry and the `yacplayer.exe` application execution alias.
- Run Windows App Certification Kit against the final Partner Center-identity package.
- Complete listing text, age rating, screenshots, support contact, privacy declaration, and restricted-capability justification. Use the public URL for `PRIVACY.md` where Partner Center requests a privacy-policy URL. Explain that `runFullTrust` is needed for the Win32 console/ANSI UI, WASAPI playback, and user-selected local music.
- Do not use the Inno Setup EXE in the MSIX submission path; raw MSI/EXE submissions have different signing requirements.

## Secret hygiene

The build consumes no secret. `.gitignore` excludes the local identity override, MSIX outputs, and common certificate/private-key formats. Partner Center identity fields are metadata, not credentials, but the override stays local to avoid accidental publisher coupling. Never put Partner Center API credentials, certificate passwords, PFX files, or private keys in source, command history, package layout, or CI logs.

## Русская памятка

Публикация в Microsoft Store не меняет лицензию MIT и не требует закрывать исходный код. Пакет содержит `LICENSE` и `THIRD_PARTY_NOTICES.md`; в карточке приложения следует дать ссылку на публичный репозиторий.

Для Store зарезервируйте имя продукта, скопируйте три точных значения со страницы **Product management → Product identity** в игнорируемый файл `packaging/msix/StoreIdentity.local.psd1`, увеличьте четырёхкомпонентную версию и запустите команду с `-UseLocalStoreIdentity`. Сертификат и PFX не нужны: принятый MSIX переподписывает Microsoft. Перед отправкой прогоните Windows App Certification Kit и заполните карточку, privacy declaration и обоснование `runFullTrust`.
