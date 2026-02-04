# BTCPay Server Translator

A command-line tool to translate BTCPay Server's UI text to multiple languages using OpenRouter AI service. Now enjoy BTCPay Server in your local languages. 

## Features

- Translates BTCPay Server's default English strings to any supported language
- Uses OpenRouter API with various AI models
- URL Support: Download translations directly from GitHub URLs
- Batch processing with configurable concurrency and rate limiting
- Secure configuration using environment variables
- Resume capability for interrupted translations
- Detailed logging and progress tracking

## Setup

### 1. Get an OpenRouter API Key

1. Go to [OpenRouter.ai](https://openrouter.ai/)
2. Sign up for an account
3. Get your API key from the dashboard

### 2. Configure Environment Variables

Copy the example environment file:

```bash
cp env.example .env
```

Edit `.env` and set your API key:

```bash
# Required: Your OpenRouter API key
OPENROUTER_API_KEY=sk-or-v1-your-actual-api-key-here
# Optional: Override default settings
OPENROUTER_MODEL=add_your_model
OPENROUTER_BASE_URL=https://openrouter.ai/api/v1
OPENROUTER_SITE_NAME=BTCPayTranslator
OPENROUTER_APP_NAME=https://github.com/btcpayserver/btcpayserver
```

## Usage

### List Available Languages
```bash
dotnet run -- list-languages
```

### Translate to a Single Language
```bash
# Translate to Hindi
dotnet run -- translate --language hi

# Force retranslation of all strings
dotnet run -- translate --language hi --force
```

### Batch Translation to Multiple Languages
```bash
# Translate to multiple languages
dotnet run -- batch --languages hi es fr de

# Continue on error (don't stop if one language fails)
dotnet run -- batch --languages hi es fr de --continue-on-error

# Force retranslation
dotnet run -- batch --languages hi es fr de --force
```

### Check Translation Status
```bash
dotnet run -- status
```

### Update Existing Translation with New Strings
```bash
# Update Hindi translation with latest strings from GitHub
dotnet run -- update --language hi
```

The `update` command:
- Fetches the latest strings from BTCPayServer's GitHub repository
- Compares with your local translation file
- Only translates new strings that were added
- Removes strings that were deleted from the source
- Preserves all existing translations
- Maintains the same order as the source file

This is useful when BTCPayServer adds new features and strings. Instead of retranslating everything, you can just update with the new additions.

## Supported Languages

The tool supports 100+ languages including:

- **Major Languages**: Spanish (es), French (fr), German (de), Italian (it), Portuguese (pt), Russian (ru), Japanese (ja), Korean (ko), Chinese (zh-cn, zh-tw), Arabic (ar), Hindi (hi)
- **European Languages**: Dutch, Swedish, Norwegian, Danish, Finnish, Polish, Czech, Hungarian, etc.
- **Asian Languages**: Bengali, Tamil, Telugu, Malayalam, Thai, Vietnamese, Indonesian, etc.
- **Regional Languages**: Catalan, Basque, Welsh, Irish, Scottish Gaelic, etc.

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `OPENROUTER_API_KEY` | (required) | Your OpenRouter API key |
| `OPENROUTER_MODEL` | `anthropic/claude-3.5-sonnet` | AI model to use | best models recommended are claude 4 or gpt 5
| `OPENROUTER_BASE_URL` | `https://openrouter.ai/api/v1` | OpenRouter API base URL |
| `OPENROUTER_SITE_NAME` | `BTCPayTranslator` | Site name for analytics |
| `OPENROUTER_APP_NAME` | `https://github.com/btcpayserver/btcpayserver` | App name for analytics |

### Application Settings (appsettings.json)

```json
{
  "Translation": {
    "BatchSize": 50,
    "MaxRetries": 3,
    "DelayBetweenRequests": 1000,
    "InputFile": "https://raw.githubusercontent.com/btcpayserver/btcpayserver/master/BTCPayServer/Services/Translations.Default.cs",
    "OutputDirectory": "translations"
  }
}
```

**Input File Configuration:**
- **URL Support**: You can use either a local file path or a URL to the BTCPayServer translations file
- **GitHub URLs**: The tool automatically converts GitHub blob URLs to raw URLs for direct content access
- **Examples**:
  - URL: `https://raw.githubusercontent.com/btcpayserver/btcpayserver/master/BTCPayServer/Services/Translations.Default.cs`
  - Local: `../BTCPayServer/Services/Translations.Default.cs`

## Output

Translated files are saved to the configured output directory with the following structure:
```
translations/
├── hindi.json
├── spanish.json
├── french.json
└── ...
```

Each translation file includes:
- All translated strings
- Metadata about the language
- Progress reports and error logs

## Help us make it better

All the translations are AI generated and AI can make mistakes sometimes, so if you recognize a string that might need to be edited, share a pull request. 

