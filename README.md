# PsToXbox 🎮

Um utilitário de conversão de controles simples e eficiente para Windows. Ele pega os sinais de controles de PlayStation e adaptadores genéricos e os converte nativamente para um controle virtual de **Xbox 360**.

Se você está cansado de jogos de PC que só reconhecem controles de Xbox e não suportam nativamente o seu DualSense, DualShock 4 ou até mesmo controles antigos de PS2, este aplicativo resolve isso com um único clique.

## ✨ Recursos

- **Plug & Play Universal:** Suporta controles oficiais (PS3, PS4, PS5) e adaptadores antigos de PS1/PS2.
- **Conexão USB e Bluetooth:** Lê relatórios de dados nativos tanto via cabo USB quanto sem fio (Bluetooth).
- **Sem "Input Duplo" (HidHide):** Embutido com suporte ao HidHide para ocultar o controle físico original, evitando que o jogo receba dois comandos ao mesmo tempo (o controle de PS e o de Xbox virtual).
- **Instalação Automática 100% Offline:** Os drivers necessários de nível de kernel (`ViGEmBus` e `HidHide`) já estão **embutidos** no executável! Se o PC não tiver, o app extrai da própria barriga e instala de forma rápida e silenciosa.
- **Portátil e Simples:** Distribuído como um único arquivo `.exe` (Single-File). Nenhum emaranhado de `.dll`s.
- **Mapeamento Personalizado:** Possui uma aba de aprendizado para remapear botões fisicamente trocados em adaptadores genéricos chineses.
- **Dark Mode Moderno:** Interface de usuário limpa, responsiva e com suporte a teste visual dos analógicos e vibração (Rumble).

## 🚀 Como Usar

1. **Baixe** o `PsToXbox.exe` na aba [Releases](../../releases) (ou compile você mesmo).
2. **Abra** o aplicativo (nenhuma instalação principal é necessária).
3. **Instale os Drivers (Se pedido):** Se o seu PC não tiver o driver de Xbox Virtual, o app pedirá permissão de Administrador para instalá-lo secretamente em poucos segundos. Opcionalmente, ative a caixinha do "HidHide" para evitar input duplo.
4. **Conecte** seu controle via USB ou pareie via Bluetooth.
5. Clique em **Converter para Xbox 360**. A luz mudará para azul!
6. Jogue! O jogo achará que você tem um controle de Xbox autêntico conectado.

## 🛠️ Como Compilar do Código Fonte

Este projeto foi construído em **C# / .NET 8 (WPF)**.

### Pré-requisitos:
- .NET 8 SDK
- Visual Studio 2022 ou VS Code

### Passo a passo:
```bash
# Clone o repositório
git clone https://github.com/SEU-USUARIO/PsToXbox.git
cd PsToXbox

# (Importante) O projeto exige os executáveis do ViGEmBus e HidHide na raiz 
# do projeto para serem embutidos (EmbeddedResource). Eles já estão inclusos no repositório.

# Compile uma versão única (Single-File Executable)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
O executável final estará na pasta `bin\Release\net8.0-windows\win-x64\publish\`.

## ⚙️ Tecnologias Utilizadas
- [**HidSharp**](https://github.com/zeromike/HidSharp) - Para comunicação USB/Bluetooth de baixo nível.
- [**ViGEmBus**](https://github.com/nefarius/ViGEmBus) (Nefarius) - Driver embutido para criar o joystick virtual de Xbox 360 no Kernel do Windows.
- [**HidHide**](https://github.com/nefarius/HidHide) (Nefarius) - Driver embutido para esconder dispositivos HID do sistema operacional.

## ⚖️ Licença
Este projeto é distribuído sob a licença MIT. Você é livre para modificar e distribuir!
