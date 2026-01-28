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

---

## 🏗️ Propuesta de Refactorización Arquitectónica

> [!IMPORTANT]
> **Nueva documentación disponible**: Se ha creado una propuesta completa de reorganización arquitectónica aplicando **Clean Architecture** y **principios SOLID**.

### 📚 Documentación Completa

Toda la documentación está disponible en [`doc/migrate/`](./doc/migrate/):

- **[📊 Resumen Ejecutivo](./doc/migrate/resumen_ejecutivo.md)** - Vista general de la propuesta (5 min)
- **[🏗️ Arquitectura Propuesta](./doc/migrate/arquitectura_propuesta.md)** - Diseño detallado (20 min)
- **[💻 Ejemplos de Refactorización](./doc/migrate/ejemplos_refactorizacion.md)** - Código antes/después (15 min)
- **[🗺️ Roadmap Ejecutable](./doc/migrate/roadmap_ejecutable.md)** - Plan paso a paso (10 min)
- **[📘 Mejores Prácticas](./doc/migrate/mejores_practicas.md)** - Guía de estándares (referencia)
- **[🖼️ Diagrama de Arquitectura](./doc/migrate/arquitectura_comparacion_1769629057128.png)** - Comparación visual

### 🎯 Inicio Rápido

1. Lee el **[Índice Maestro](./doc/migrate/00_INDICE.md)** para navegar toda la documentación
2. Comienza con el **[Resumen Ejecutivo](./doc/migrate/resumen_ejecutivo.md)** para entender el panorama general

### 📊 Problema Identificado

- **MainWindow.xaml.cs tiene 3,637 líneas** (God Object anti-pattern)
- Alto acoplamiento entre componentes
- Difícil de mantener y extender
- Sin tests unitarios

### 💡 Solución Propuesta

- Aplicar **Clean Architecture** con 4 capas bien definidas
- Reducir MainWindow a **~200 líneas** (94% menos)
- Implementar **MVVM** completo
- Cobertura de tests **>70%**
- Desarrollo más rápido y menos bugs

### ⏱️ Cronograma Estimado

**4-6 semanas** divididas en 5 fases:
1. Fundamentos (3-5 días)
2. Infraestructura (5-7 días)
3. Use Cases (7-10 días)
4. ViewModels (5-7 días)

**👉 [Ver documentación completa →](./doc/migrate/00_INDICE.md)**
