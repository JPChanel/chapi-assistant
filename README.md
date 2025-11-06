# chapi-assistant

# Generador de Proyecto desde Base 📦🚀

- Este programa de escritorio en **.NET Core / WinForms** permite crear rápidamente un nuevo proyecto clonando una base predefinida desde Git, personalizándola automáticamente y configurándola como un nuevo repositorio Git.
---
## ✨ Características

- Clona una base de proyecto desde un repositorio Git (`api-base`)
```
git remote add origin https://gitlab.com/net-core2/api-base.git

```

- Renombra carpetas y archivos reemplazando el nombre base por el nuevo
- Reemplaza textos dentro de los archivos (por ejemplo, namespaces o nombres de proyecto)
- Elimina automáticamente la carpeta `.git` del repositorio base
- Inicializa un nuevo repositorio Git (opcional)
- Permite asociar un nuevo repositorio remoto (GitHub, GitLab, etc.)
- Muestra el progreso de todas las operaciones en una interfaz visual

---

## 🛠️ Requisitos

- [.NET 8.0 SDK o superior](https://dotnet.microsoft.com/)
- Git instalado y agregado al `PATH` del sistema
- Windows (para la versión WinForms)

---

## 🚀 ¿Cómo se usa?

1. Abrí el programa.
2. Ingresá el nombre del nuevo proyecto.
3. Seleccioná (o dejá predefinido) el repositorio base a clonar.
4. Esperá que el programa:
   - Clone la base
   - Renombre carpetas y archivos
   - Reemplace referencias internas
   - Inicialice un nuevo repo Git
5. Confirmá si querés asociar un repo remoto.

¡Y listo! Tu nuevo proyecto estará listo para desarrollar y subir a Git.

---

## 📁 Estructura esperada del proyecto base

- `Controllers/`
- `Application/`
- `Domain/`
- `Infrastructure/`
- `*.sln`

---

## 🧑‍💻 Autor

Creado por [Johan Chanel][@_chanel](https://gitlab.com/_chanel)

---

## 📝 Licencia

MIT
