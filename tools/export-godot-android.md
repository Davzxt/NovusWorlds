# Exportar Novus Worlds Client para Android

O client Godot tem preset `Android` em `godot-client/export_presets.cfg` e controles touch.

Requisitos:

- Godot .NET 4.6.2
- Godot export templates 4.6.2
- Java JDK 17 ou superior
- Android SDK com platform-tools 35+, build-tools 35.0.1, platform Android 35, CMake 3.10.2.4988404 e NDK 28.1.13356709

Com tudo configurado:

```powershell
npm run godot:export:android
```

Saida esperada:

```text
build/android/NovusWorldsClient.apk
```

Para publicar na Play Store, crie um keystore de release e configure o preset Android no Godot.

Nota atual: em 10/05/2026, o exportador Godot 4.6.2 .NET instalado nesta maquina ainda bloqueou o APK com a mensagem
`Exporting to Android when using C#/.NET is experimental.` mesmo com SDK, JDK, NDK, CMake, Gradle template e keystore
configurados. O preset e o script ficam prontos, mas para gerar APK agora existem dois caminhos praticos:

- converter o client Android para GDScript, mantendo o client Windows em C#;
- usar uma versao/build do Godot .NET que aceite export Android C# nesse ambiente.
