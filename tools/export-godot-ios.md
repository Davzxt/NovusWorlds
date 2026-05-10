# Exportar Novus Worlds Client para iOS

Godot 4.6 suporta C# em iOS, mas o suporte ainda e experimental e a exportacao para iOS precisa ser feita em macOS com Xcode.

Requisitos:

- macOS
- Godot .NET 4.6.2
- .NET SDK 8 x64/arm64
- Xcode
- Apple Developer Team ID
- Godot export templates instalados

Passos:

1. Copie o repositorio para o Mac.
2. Abra `godot-client/project.godot` no Godot .NET.
3. Abra `Project > Export`.
4. Selecione o preset `iOS`.
5. Preencha `App Store Team ID`, bundle identifier e provisioning profiles.
6. Exporte para `build/ios/NovusWorldsClient.zip`.
7. Abra o projeto exportado no Xcode.
8. Assine e rode no iPhone, ou gere build para TestFlight/App Store.

Observacao: o client mobile ja tem joystick virtual, botao de pulo e camera por toque.
