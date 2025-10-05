# Contributing Guidelines

Thank you for considering contributing!

## Getting Started
1. Fork the repo
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Install .NET 8 and run `dotnet restore`
4. Use `settings.json.template` to create a local `settings.json`

## Pull Requests
- Keep PRs focused (one feature/fix)
- Include a clear description
- Ensure `dotnet build -c Release` succeeds
- Prefer small commits with descriptive messages

## Coding Style
- C# 10+ features allowed
- Nullable reference types enabled
- Use async/await and cancellation tokens

## Commit Message Format
```
<type>: <short summary>

Types: feat, fix, chore, docs, refactor, test
```

## Security / Secrets
Do NOT commit real credentials, tokens, or `token.json`.

## Issues
Include reproduction steps, expected vs actual, environment details.

## Roadmap Ideas
- Forward email implementation
- Multi-account support
- Enhanced label editing
- Attachment handling

Thanks for contributing!
