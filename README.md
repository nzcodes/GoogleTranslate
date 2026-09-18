# Google Translate for PowerToys Run

Google Translate plugin for PowerToys.

![pic](Images/sample-pic.jpg)

## Build
- Install .NET from `aka.ms/dotnet/download`
- run build.bat
- copy output/GoogleTranslate to `%localappdata%\Microsoft\PowerToys\PowerToys Run\Plugins\`

## Install
- Download .rar from `Releases`

## Features
- **Instant Translation**: Type `tr <word>` to translate into your default language (EN).
- **Target Specific Language**: Type `tr hello to spanish`, `tr bonjour en`, or `tr es: good morning`.
- **Phonetics & Pronunciation**: Displays romanization/pronunciation guides for Japanese, Chinese, Arabic, Russian, and more.
- **Zero-Config Web API**: Works out-of-the-box using standard Google Translate endpoints without requiring an expensive billing API key.
- **In-Memory Caching**: Avoids repeated network queries for repeated terms.


## Usage

| Query | What it does |
|---|---|
| `tr apple` | Translates "apple" into target language (en) |
| `tr good morning to es` | Translates "good morning" to Spanish (`es`) |
| `tr wunderschön en` | Translates German "wunderschön" to English (`en`) |
| `tr ありがとう` | Translates Japanese "arigatou" to English |
| `tr fr: how are you?` | Prefix syntax to translate into French |
