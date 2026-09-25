# BackupToDrive

Consola de .NET 8 que puede pedirle un backup a SQL Server, buscar archivos en una carpeta local y subirlos a Google Drive. La primera vez inicia sesión con tu Gmail. Los archivos quedan en tu Drive, dentro de tu cuota.

## Requisitos

- .NET 8
- Una cuenta de Gmail
- Un proyecto en Google Cloud. La prueba gratis de USD 300 no hace falta: si aparece el cartel, tocá **Descartar** y no **Comenzar gratis**.

## Conectar con tu Gmail

1. Entrá a [crear un proyecto](https://console.cloud.google.com/projectcreate), logueado con tu Gmail, y creá uno (por ejemplo `backup-to-drive`).
2. En el buscador de arriba de la consola escribí **Google Drive API** y habilitala en ese proyecto.
3. Abrí **Google Auth Platform**. El menú viejo de Credenciales ahora se llama **Clientes**.

   - **Información de la marca.** Nombre de la app: `BackupToDrive`. Correo de asistencia: tu Gmail. Guardá.
   - **Público.** Dejalo en **Externo** y en estado **En prueba**. En usuarios de prueba agregá tu mismo Gmail.
   - **Acceso a los datos.** Agregá el permiso `https://www.googleapis.com/auth/drive` y guardá.
   - **Clientes** (o el botón **Crear cliente de OAuth**). Tipo: **Aplicación de escritorio**. Nombre: `BackupToDrive`. Creá y descargá el JSON.

4. Renombrá el archivo descargado a `client_secret.json` y guardalo en `BackupToDrive/credentials/`. Esa carpeta está en `.gitignore`. No lo subas a git ni lo pegues en un chat.

## Configuración

Copiá `BackupToDrive/appsettings.Example.json` a `BackupToDrive/appsettings.json` y completá los valores. Cada clave está explicada en el ejemplo: Drive, la connection string de SQL Server, los pasos del job, WinRAR y el mail. `appsettings.json` está en `.gitignore`.

## Probar

El job de ejemplo lee `C:\Backups` y toma archivos `*.bak`, `*.sql`, `*.zip`, `*.rar` y `*.7z` con más de 5 minutos de antigüedad. Copiá ahí un archivo de prueba antes de correr.

Desde la raíz del repo:

```powershell
dotnet run --project BackupToDrive -- --dry-run
dotnet run --project BackupToDrive
```

`--dry-run` solo lista lo que subiría. La segunda corrida abre el navegador. Google muestra "Google no verificó esta app": **Avanzada** y después **Continuar**. Es esperable mientras la app siga en modo de prueba y el usuario sea tu Gmail.

El token queda en `BackupToDrive/credentials/oauth-token/` y tampoco se commitea. Las corridas siguientes no vuelven a pedir login.

Para un solo job: `dotnet run --project BackupToDrive -- --job Default`.
