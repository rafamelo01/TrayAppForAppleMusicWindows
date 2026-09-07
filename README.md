# Apple Music Hotkeys

Miniapp para Windows que adiciona atalhos globais para controlar o Apple Music em segundo plano, sem depender do PowerToys. Também esconde a janela do Apple Music na bandeja enquanto a reprodução e os processos continuam ativos.

## Atalhos

| Atalho | Ação |
| --- | --- |
| `F2` | Reproduzir ou pausar a mídia ativa do Windows |
| `Ctrl + Alt + ↑` | Aumentar o volume do Apple Music em 2 pontos percentuais |
| `Ctrl + Alt + ↓` | Diminuir o volume do Apple Music em 2 pontos percentuais |
| `Ctrl + Alt + M` | Mostrar ou ocultar a janela do Apple Music |

O ajuste de volume afeta somente as sessões de áudio do Apple Music no mixer do Windows. Outros aplicativos e o volume geral do dispositivo não são alterados.

**F2 envia um comando global de mídia:** pode controlar outro player se ele estiver como mídia ativa. Enquanto o miniapp estiver rodando, F2 fica reservado para essa função.

Cada toque executa uma ação. Manter a tecla pressionada não repete o comando automaticamente.

## Requisitos

- Windows de 64 bits. Testado no Windows 11.
- .NET Framework 4.x com Windows Forms.
- Apple Music para Windows instalado pela Microsoft Store, com uma sessão de áudio disponível.
- Uma pasta com permissão de escrita para salvar as preferências e o diagnóstico local.

Não requer PowerToys, AutoHotkey ou pacotes NuGet.

## Como usar

1. Coloque `AppleMusicTray.exe` em uma pasta permanente, por exemplo `%LOCALAPPDATA%\AppleMusicHotkeys`.
2. Execute o aplicativo. Um ícone musical rosa aparecerá na bandeja, possivelmente dentro da área de ícones ocultos do Windows.
3. Abra o Apple Music e reproduza uma música.
4. Use os atalhos mesmo quando estiver trabalhando em outro aplicativo.
5. Use `Ctrl + Alt + M` ou clique duas vezes no ícone da bandeja para esconder ou recuperar a janela do Apple Music.

Para deixar o controle de volume concentrado no mixer, mantenha a barra interna do Apple Music em um nível fixo, como 100%, e ajuste o volume pelos atalhos. A barra dentro do Apple Music não acompanha as alterações feitas no mixer.

Quando a janela é ocultada, ela desaparece da barra de tarefas, mas `AppleMusic.exe` e seus processos auxiliares continuam abertos. Por isso a reprodução não é interrompida.

O botão **X** do Apple Music ainda encerra o aplicativo e interrompe a reprodução. Para obter o comportamento de “fechar para a bandeja”, use `Ctrl + Alt + M`, clique duas vezes no ícone ou ative **Ocultar Apple Music ao minimizar** e use o botão de minimizar.

Antes de usar, remova remapeamentos desses mesmos atalhos de outros programas, incluindo o PowerToys, para evitar conflitos.

## Menu da bandeja

Clique com o botão direito no ícone para acessar:

- **Mostrar Apple Music:** restaura a janela; se o aplicativo não estiver aberto, tenta iniciá-lo.
- **Ocultar Apple Music:** remove sua janela da tela e da barra de tarefas sem encerrar a reprodução.
- **Alternar janela:** mostra ou oculta a janela, como `Ctrl + Alt + M`.
- **Ocultar Apple Music ao minimizar:** transforma o botão de minimizar em uma forma de enviar a janela para a bandeja.
- **Reproduzir / pausar:** envia o mesmo comando de F2.
- **Aumentar volume / Diminuir volume:** ajusta somente o Apple Music.
- **Iniciar com o Windows:** ativa ou desativa a inicialização automática para o usuário atual.
- **Ocultar ícone (manter atalhos):** remove o ícone da bandeja sem encerrar o aplicativo.
- **Como mostrar o ícone novamente:** exibe instruções de recuperação.
- **Sair (desativar atalhos):** encerra o miniapp e libera as teclas.

Para recuperar o ícone oculto, **execute `AppleMusicTray.exe` novamente**. Se você criou um atalho para ele no menu Iniciar, também pode usá-lo. A segunda execução apenas mostra o ícone da instância existente.

O estado oculto é lembrado quando o aplicativo inicia com o Windows. Sair do aplicativo não desativa a inicialização automática.

## Iniciar com o Windows

No menu da bandeja, marque **Iniciar com o Windows**. A opção registra o executável com o argumento `--startup` na chave do usuário:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
Valor: AppleMusicHotkeys
```

Escolha uma pasta permanente antes de ativar essa opção. Se mover o executável, abra-o na nova localização e desmarque e marque novamente a opção para atualizar o caminho.

## Compilar

O projeto utiliza dois arquivos C# e pode ser compilado pelo compilador do .NET Framework, sem Visual Studio ou um arquivo `.csproj`.

Na pasta do repositório, execute no PowerShell:

```powershell
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Compilador do .NET Framework não encontrado.'
}
New-Item -ItemType Directory -Path '.\build' -Force | Out-Null
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /main:TrayProgram /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:build\AppleMusicTray.exe AppleMusicTray.cs Audio.cs
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
```

O resultado fica em `build\AppleMusicTray.exe`. Para atualizar uma instalação existente, use **Sair** no menu da bandeja antes de substituir o executável e abra-o novamente depois da cópia.

## Linha de comando

```powershell
.\AppleMusicTray.exe                 # Inicia ou mostra o ícone da instância existente
.\AppleMusicTray.exe --show          # Mostra o ícone
.\AppleMusicTray.exe --hide          # Inicia oculto ou oculta a instância existente
.\AppleMusicTray.exe --startup       # Inicia respeitando a preferência de visibilidade
.\AppleMusicTray.exe --exit          # Encerra a instância existente
.\AppleMusicTray.exe --volume-up     # Aumenta o volume pela instância existente
.\AppleMusicTray.exe --volume-down   # Diminui o volume pela instância existente
.\AppleMusicTray.exe --toggle-music  # Mostra ou oculta a janela do Apple Music
.\AppleMusicTray.exe --show-music    # Mostra ou inicia o Apple Music
.\AppleMusicTray.exe --hide-music    # Oculta a janela sem encerrar a reprodução
```

Os comandos de volume exigem uma instância já em execução. O argumento `--startup` não cadastra o início automático; use a opção do menu para isso.

## Estrutura e funcionamento

| Arquivo | Responsabilidade |
| --- | --- |
| `AppleMusicTray.cs` | Bandeja, atalhos globais, instância única, início automático e comunicação entre instâncias |
| `Audio.cs` | Identificação das sessões do Apple Music e ajuste de volume pelo Windows Core Audio |
| `AppleMusicTray.exe` | Executável compilado |

Os atalhos usam `RegisterHotKey` com `MOD_NOREPEAT`. O volume é processado em uma thread separada e limitado ao intervalo de 0% a 100%. A janela do Apple Music é localizada pelo processo `AppleMusic.exe` e ocultada com a API `ShowWindow` do Windows.

Para manter o menu da bandeja responsivo, a preferência de ocultar ao minimizar permanece em memória durante a execução. O monitor só fica ativo quando essa opção está ligada, verifica a janela uma vez por segundo e reutiliza os identificadores encontrados por um curto período. O ajuste de volume continua fora da thread da interface.

O controle reconhece `AppleMusic.exe` e o auxiliar `AMPLibraryAgent.exe`, verificando se pertencem ao pacote `AppleInc.AppleMusicWin_`. Em cada dispositivo de saída, usa a primeira sessão ativa encontrada como referência e aplica o mesmo nível às demais sessões do Apple Music. Se não houver sessão ativa, usa a primeira sessão disponível do aplicativo.

## Arquivos locais

O aplicativo grava estes arquivos ao lado do executável:

- `hidden.txt`: preferência de visibilidade do ícone.
- `hide-on-minimize.txt`: preferência para ocultar a janela do Apple Music quando ela for minimizada.
- `status.txt`: estado da instância, registro dos atalhos, última ação e eventual erro.
- `startup-error.txt`: diagnóstico de falha na inicialização, quando houver.

São arquivos da instalação local; não são necessários para compilar ou distribuir o código. Recomenda-se ignorá-los no Git, assim como a pasta `build/` e backups. Os diagnósticos não incluem nomes de músicas, credenciais ou conteúdo da biblioteca.

## Solução de problemas

**O ícone desapareceu:** execute o app novamente para recuperá-lo. Verifique também a área de ícones ocultos da barra de tarefas.

**Os atalhos não foram registrados:** feche outros programas que usem as mesmas combinações e remova remapeamentos equivalentes do PowerToys. `status.txt` deve indicar `registered=4` quando os quatro atalhos estiverem disponíveis.

**A janela não reaparece:** pressione `Ctrl + Alt + M` ou escolha **Mostrar Apple Music**. Se o processo tiver sido encerrado pelo X, o miniapp inicia uma nova instância, mas a reprodução anterior não é retomada automaticamente.

**A barra interna do Apple Music não muda:** o ajuste é feito no mixer do Windows, independentemente dessa barra.

**Diminuir o volume deixou a música sem som:** o volume chega a zero no limite inferior. Use o atalho de aumentar; cada toque soma 2 pontos percentuais.

**Não há ajuste de volume:** confirme que o Apple Music está aberto e já iniciou a reprodução. O utilitário não abre o player nem cria uma sessão de áudio quando ela não existe.

## Remover

1. Desmarque **Iniciar com o Windows**.
2. Selecione **Sair (desativar atalhos)**.
3. Remova a pasta da instalação e eventuais atalhos que você criou.
