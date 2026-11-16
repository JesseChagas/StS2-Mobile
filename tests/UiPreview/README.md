# Validação visual e de gestos

Projeto isolado de preview, sem login real, sem conexão Steam e sem bibliotecas do jogo. Usa os componentes reais da interface. Exige Godot Mono 4.5.1 e SDK .NET 9.

Na raiz do projeto:

```
dotnet build tests/UiPreview/UiPreview.csproj
godot --path tests/UiPreview -- scroll dark pt
godot --path tests/UiPreview -- interaction dark pt
```

Substitua godot pelo executável Mono instalado. O cenário scroll injeta eventos de mouse/toque sobre um botão e verifica rolagem, preservação do clique curto, ausência de acionamento acidental, menu à esquerda com recuo, identidade fixa e bloqueio por modal. As capturas ficam na pasta de dados desse projeto de preview, dentro de Previews. Opcionalmente passe um caminho de saída como quarto argumento após --.
