namespace Chapi.Infrastructure.AI;

public class GetPrompt
{
    public static string GitCommit(string request)
    {
        return $@"
        Analiza el siguiente 'diff' y genera un mensaje de commit profesional en español según el estándar Conventional Commits.

        ?? Reglas:
        - Debes retornar SÓLO un objeto JSON válido, sin formato markdown (```json).
        - El JSON debe tener esta estructura:
          {{
            ""summary"": ""tipo(alcance): resumen corto"",
            ""description"": ""descripción detallada de los cambios""
          }}
        - 'summary' (Resumen): Una sola línea, 72 caracteres máximo.
        - 'description' (Descripción):
            - Un resumen de 1 o 2 frases sobre el 'por qué' del cambio.
            - Seguido de una lista de viñetas (usando '-') con los cambios más importantes.
            - Si los cambios son muy pequeños, la descripción puede ser un string vacío ("""").

        ?? Guía de Tipos:
        - 'feat': Nuevas funciones.
        - 'fix': Corrección de errores.
        - 'refactor': Limpieza de código sin cambiar funcionalidad.
        - 'chore': Tareas de mantenimiento, builds, etc.

        Ejemplo de Salida JSON:
        {{
          ""summary"": ""feat(git): implementa generación de commit con IA y manejo de diff"",
          ""description"": ""Se actualiza el flujo de commits para usar la IA.\n\n- Modifica btnGitCommit_Click para analizar solo archivos seleccionados.\n- Cambia el comando diff a 'diff HEAD' para evitar el stage.\n- Añade deserialización para el nuevo formato JSON de respuesta.""
        }}

        Texto del diff (Contexto):
        {request}
        ";

    }
    public static string AnalyzeEmail(string moduleName, string methodName, string emailContent, string dataBase, string tipoMetodo)
    {
        return $@"
            Analiza el siguiente correo técnico o procedimiento Almacenado y extrae la información del Stored Procedure.

            CONTEXTO:
            - Módulo: {moduleName}
            - Método: {methodName}
            - Tipo Metodo: {tipoMetodo}

            CORREO TÉCNICO:
            {emailContent}

            INSTRUCCIONES:
            Retorna SOLO un JSON con esta estructura exacta (sin markdown, sin explicaciones):

            {{
              ""StoredProcedureName"": ""nombre_del_sp :AN_COD_VISITA, :AN_NOMBRE, :AN_NUM_SECUEN"",
              ""RequestParameters"": [
                ""public int code {{ get; set; }}"",
                ""public string name {{ get; set; }}"",
                ""public DateTime? startDate {{ get; set; }}""
              ],
              ""Parameters"":[
                AN_COD_VISITA = request.code,
                AN_NOMBRE = request.name,
                AN_NUM_SECUEN = request.sequenceNumber
              ],
              ""DtoFields"": [
                ""public int pro_codigo {{ get; set; }}"",
                ""public string pro_nombre {{ get; set; }}"",
                ""public decimal pro_precio {{ get; set; }}""
              ],
              ""ResponseMapper"": [
                ""code = dto.pro_codigo"",
                ""name = dto.pro_nombre"",
                ""price = dto.pro_precio""
              ]
            }}

            REGLAS:
            1. StoredProcedureName: nombre exacto del SP mencionado incluido su esquema seguido de los parametros de consulta si viene con comillas u otros limpialo , solo devuelve procedure limpio ejm: OCMAERP.SP_CONTRATO_RPTE_CONTRATO :AS_CONTRATO  o si  la base de datos es POSTGRES tomar estas consideraciones =>( 
                => SI el tipo de metodo es Get o ById retornar en este formato por ejm: SELECT * FROM appmovil.f_appmovil_elevados_magistrado_deta(@as_cod_usuario,@an_cod_nivacc,@an_cod_magistrado,@an_cod_distri)
                => SI el tipo de metodo es Post,Put o Delete solo retornar el nombre del sp ejm: seguridaderp.sp_segerp_rol_mant
            )
            2. RequestParameters: parámetros de entrada (tipo nombre) equivalente en ingles en camelCase, si no se puede ver el tipo de dato infiere y ponle el tipo; siempre debes darme estandar de .netCore en ingles ""public int parametro {{ get; set; }}""
            3. Parameters: mapea el requestParameter a lo q espera el SP y hace su mapeo automatico ; ahora si la base de datos es POSTGRES y es un Post,Put o Delete  el Parameter q retorne en este formato ejm: =>(
            parameters.Add(""@an_cod_rol"", datos.n_cod_rol);
            parameters.Add(""@as_des_rol"", datos.s_des_rol);
            parameters.Add(""@as_accion"", datos.s_accion);
            parameters.Add(""@rn_codigo"", dbType: DbType.Int32, direction: ParameterDirection.Output);
            parameters.Add(""@rs_valor"", dbType: DbType.String, direction: ParameterDirection.Output, size: 100);
            )
            4. DtoFields: campos que retorna la BD (tipo nombreCampo) todo en minusculas tal cual el valor de retorno de la bd, si no se puede ver el tipo de dato infiere y ponle el tipo;  siempre debes darme estandar de .netCore ""public string parametro {{ get; set; }}""
            5. ResponseMapper: mapeo para objeto anónimo en ingles camelCase (propiedadAPI = dto.campo)
            6. Usa tipos C#: int, string, decimal, DateTime, bool            
            7. NO inventes datos, solo extrae lo del correo
            8. Si no hay parámetros, retorna array vacío []

            ESTÁNDARES DE NOMBRADO PERSONALIZADOS:
            - Los nombres en inglés deben seguir tus convenciones:
              • Todo campo que represente un CÓDIGO termina con **Code** (ej: resolutionCode, districtOriginCode, expedientCode).
              • Todo campo que represente un NOMBRE o DESCRIPCIÓN termina con **Name** (ej: resolutionName, magistrateName).
              • Todo campo que represente un FLA  inician con **fla** (ej: flaSigned, flaProcess).
              • Usa camelCase en todos los nombres.
              • Ejemplos de propiedades estándar que debes seguir:
                public string? code {{ get; set; }}
                public string? documentNumber {{ get; set; }}
                public string? paternalSurname {{ get; set; }}
                public string? maternalSurname {{ get; set; }}
                public string? marriedSurname {{ get; set; }}
                public string? name {{ get; set; }}
                public string? fullName {{ get; set; }}
                public string? surnames {{ get; set; }}
                public string? email {{ get; set; }}
                public string? workEmail {{ get; set; }}
                public string? phoneNumber {{ get; set; }}
                public string? address {{ get; set; }}
                public int districtOriginCode {{ get; set; }}
                public string districtOriginName {{ get; set; }}
                public int districtJudicialCode {{ get; set; }}
                public int expedientCode {{ get; set; }}
                public string expedientType {{ get; set; }}
                public string expedientYear {{ get; set; }}
                public string notes {{ get; set; }} 
                public int phaseCode {{ get; set; }}
                public int flaProcess {{ get; set; }}
                public string comprehendeds {{ get; set; }}

            REGLA FINAL:
            Adecúa los nombres generados a estos estándares si los términos del correo técnico son equivalentes o cercanos.";

    }

    public static string GenerateSqlCall(string procedureName, string dbType, string netParams)
    {
        string syntaxHelp = "";
        string exampleCall = "";

        switch (dbType)
        {
            case "Postgres (Función)":
                syntaxHelp = "La sintaxis debe ser una consulta SELECT: SELECT * FROM schema.mi_funcion(param1 => valor1, param2 => valor2);";
                exampleCall = "SELECT * FROM mi_schema.fn_buscar_usuario(an_id_usuario => 123, as_nombre => 'juan');";
                break;
            case "Postgres (SP)":
                syntaxHelp = "La sintaxis debe ser una llamada CALL: CALL schema.mi_sp(param1 => valor1, param2 => valor2);";
                exampleCall = "CALL mi_schema.sp_actualizar_stock(an_id_producto => 99, an_cantidad => 50);";
                break;
            case "Sybase (SP)":
                syntaxHelp = "La sintaxis debe ser una llamada CALL con parámetros nombrados: CALL \"schema\".\"mi_sp\"(\"param1\" = valor1, \"param2\" = valor2);";
                exampleCall = "CALL \"OCMAERP\".\"SP_SANCVAL_MANT\"(\"AN_COD_INTEXP\" = 790549, \"AS_NRO_RESO\" = '004', \"AD_FEC_MOVIMI\" = '2025-11-03');";
                break;
            default:
                syntaxHelp = "La sintaxis debe ser una llamada CALL con parámetros nombrados: CALL \"schema\".\"mi_sp\"(\"param1\" = valor1, \"param2\" = valor2);";
                exampleCall = "CALL \"OCMAERP\".\"SP_SANCVAL_MANT\"(\"AN_COD_INTEXP\" = 790549, \"AS_NRO_RESO\" = '004', \"AD_FEC_MOVIMI\" = '2025-11-03');";
                break;
        }

        return $"""
        Eres un asistente experto en SQL. Tu tarea es convertir una cadena de parámetros de .NET en una consulta SQL ejecutable para depuración.

        **Tarea:**
        1.  Recibirás un nombre de Procedimiento/Función, un tipo de BD y una lista de parámetros de .NET.
        2.  Debes generar el comando SQL para ejecutarlo.

        **Reglas Estrictas de Formateo:**
        1.  **Detección de Tipos:**
            - Si un valor es puramente numérico (ej: `50`, `0`, `790549`), trátalo como un NÚMERO (sin comillas).
            - Si un valor es CUALQUIER OTRA COSA (ej: `004`, `DSDFSDSSFSD`, `24196~1...`, `LCORDOVA`, `U`), trátalo como un STRING (con comillas simples: `'valor'`).
            - Si un valor está vacío (ej: `AS_DOCUMENTOS = ,`), trátalo como un string vacío (`''`).
            - Si un valor es una fecha/hora .NET (ej: `3/11/2025 00:00:00`), conviértelo a formato `YYYY-MM-DD` como un string (ej: `'2025-11-03'`).
        2.  **Sintaxis SQL:**
            - Sigue la sintaxis específica para el tipo de BD.
            - {syntaxHelp}
            - Ejemplo de sintaxis: {exampleCall}
        3.  **Formato de Salida:**
            - Devuelve ÚNICAMENTE el bloque de código SQL.
            - No incluyas "Respuesta:", "Aquí está el SQL:", ni ` ```sql `.
            - Formatea el SQL con saltos de línea para que sea legible, como en el ejemplo del usuario.

        **Datos de Entrada:**
        -   **Procedimiento/Función:** `{procedureName}`
        -   **Tipo de BD:** `{dbType}`
        -   **Parámetros .NET:**
            ```
            {netParams}
            ```

        **Salida (Solo SQL):**
        """;
    }

    public static string ChatAssistant(string contextInfo, string conversationHistory, string capabilitiesInfo, string userMessage)
    {
        return $@"
                Eres un asistente de desarrollo integrado en Chapi Assistant, una aplicación para gestión de proyectos y Git.

                TU PERSONALIDAD:
                - Hablas en español de forma natural y amigable
                - Eres experto en desarrollo de software, Git, arquitectura y buenas prácticas

                TUS CAPACIDADES EN ESTE PROYECTO (CHAPI):
                {capabilitiesInfo}

                REGLAS DE ACCIÓN:
                1. Si el usuario te pide hacer algo que está dentro de tus capacidades (como un commit), debes sugerir la acción si solo tienes todo claro y preparado; ello después de explicar qué harás.
                  
                  **VALIDACIÓN CRÍTICA DE PROYECTO**:
                  Si en el contexto ves '⚠️ No hay proyecto seleccionado actualmente':
                  - NO puedes ejecutar acciones de Git (commit, push, pull, branch, etc.).
                  - SÍ puedes ejecutar acciones de Gestión de Proyecto (project.create, project.clone, project.list, project.add).
                  - Si el usuario pide algo de Git y no hay proyecto, dile amablemente: 'Primero debes abrir o crear un proyecto para hacer eso.'
                2. Para sugerir una acción, incluye AL FINAL de tu respuesta (después de tu explicación) un bloque con este formato exacto:
                  [[ACTION:{{""type"":""ID_EXACTO_DE_LA_CAPACIDAD"",""params"":{{""param1"":""valor1""}}}}]]
                  
                  IMPORTANTE: Usa el ID EXACTO listado en ""TUS CAPACIDADES"" y que sea más adecuado (ej: git.commit, project.create, project.clone). NO INVENTES NOMBRES DE ACCIÓN.

                3. REGLAS ESPECÍFICAS DE ACCIONES:
                  - Para COMMIT (git.commit): Siempre genera un mensaje profesional Conventional Commits.
                    Si el usuario pide ""commit y subir"" o ""hacer push después"", agrega el parámetro ""push"": ""true"".
                    Ej: [[ACTION:{{""type"":""git.commit"",""params"":{{""message"":""feat(ui): nuevos botones"",""push"":""true""}}}}]]
                    Si solo pide commit:
                    Ej: [[ACTION:{{""type"":""git.commit"",""params"":{{""message"":""feat(core): update logic""}}}}]]
                  
                  - Para CREAR PROYECTO (project.create):
                    **USO EXCLUSIVO**: Úsalo SOLO si el usuario pide explícitamente crear una 'API', 'Backend', 'Proyecto .NET', 'Arquitectura Hexagonal' o 'Clean Architecture'.
                    Debes extraer **nombre** y **ruta**.
                    Ej: [[ACTION:{{""type"":""project.create"",""params"":{{""name"":""MiApiHexagonal"",""path"":""C:\\Ubicacion""}}}}]]
                    
                    **DESAMBIGUACIÓN IMPORTANTE**: 
                    Si el usuario solo dice 'crear un proyecto' (sin especificar tipo), NO sugieras ninguna acción todavía.
                    Pregúntale: '¿Quieres clonar un repositorio existente o crear una nueva API con Clean Architecture?'

                    Si falta el nombre O la ruta, PREGÚNTALE al usuario. NO uses valores por defecto.

                  - Para CLONAR PROYECTO (project.clone):
                    Debes extraer **URL** y **ruta**.
                    Ej: [[ACTION:{{""type"":""project.clone"",""params"":{{""url"":""https://github.com/u/repo.git"",""path"":""C:\\Ubicacion""}}}}]]
                    Si falta la URL o la ruta, PREGÚNTALE al usuario.
                4. IMPORTANTE: Sólo sugiere la acción si el usuario la pidió o es el siguiente paso lógico. No realices acciones sin preguntar si el usuario no fue explícito.

                {contextInfo}

                {conversationHistory}

                === MENSAJE ACTUAL DEL USUARIO ===
                {userMessage}
                ";
    }
    public static string DocSection(string sectionTitle, string projectContext)
    {
        return $@"
        Eres un experto en documentación de software. Redacta el contenido de la sección ""{sectionTitle}"" 
        para un documento técnico de ingeniería de software.
        
        CONTEXTO DEL PROYECTO:
        {projectContext}

        REGLAS:
        - Responde SOLO con el contenido en Markdown bien estructurado.
        - NO incluyas encabezados H1 (título principal).
        - Sé técnico, conciso y profesional. 
        - Máximo 300 palabras.
        ";
    }

    public static string DocDiagram(string sectionTitle, string format, string projectContext)
    {
        var formatInstructions = format.ToLower() == "mermaid"
            ? "Usa la sintaxis Mermaid correcta. Empieza con el tipo: classDiagram, sequenceDiagram, graph LR, erDiagram, etc."
            : "Usa la sintaxis PlantUML correcta. Incluye @startuml y @enduml.";

        return $@"
        Genera código {format} para el diagrama de ""{sectionTitle}"" basado en este contexto de proyecto:
        
        CONTEXTO:
        {projectContext}

        REGLAS ESTRICTAS:
        1. Responde SOLO con el código {format}, sin explicación, sin bloques de markdown (```).
        2. {formatInstructions}
        3. El código debe ser válido y renderizable.
        4. Usa nombres reales del proyecto si están disponibles en el contexto.
        5. Mantén el diagrama enfocado y claro, máximo 15 elementos.
        ";
    }

    public static string DocMetadata(string jsonKeys, string projectContext, string userPrompt)
    {
        return $@"
        Eres un arquitecto de software experto documentando sistemas.
        Tu tarea es llenar una plantilla de metadatos JSON basada en el contexto del proyecto y la instrucción del usuario.

        CONTEXTO:
        {projectContext}

        INSTRUCCIÓN:
        {userPrompt}

        INSTRUCCIONES CLAVES:
        1. Devuelve ÚNICAMENTE un objeto JSON válido (sin etiquetas markdown como ```json).
        2. El JSON debe contener EXACTAMENTE las claves proporcionadas en la lista a continuación, y como valor el contenido generado.
        2.1 Para claves no estructurales que no sean de imagen (ej. INTRODUCCION, CAPA_DESC, PQ_DESC), no dejes valores vacíos.
        2.2 Si existe INTRODUCCION: redacta 2 parrafos formales (120-220 palabras en total), con contexto institucional, objetivo del documento y valor tecnico de los diagramas.
        2.3 Si existe OBJETIVOS: genera entre 4 y 7 objetivos en formato lista con viñetas, claros y accionables, alineados al sistema y su normativa.
        2.4 Si existe ALCANCE: redacta 2 parrafos formales (120-220 palabras en total), delimitando alcance funcional, alcance tecnico, integraciones, restricciones y ambito de despliegue.
        2.5 Evita textos de una sola linea en INTRODUCCION/OBJETIVOS/ALCANCE. Deben ser explicativos y con lenguaje tecnico profesional.
        3. Para claves que contengan 'IMG' o 'DIAGRAMA' en su nombre (ej. IMG_ARQUITECTURA), el valor DEBE ser código válido de PlantUML (sin bloques ```plantuml).
        4. Para claves que terminen en '_ITEMS', el valor DEBE ser un JSON ARRAY (no string) de objetos.
        5. Caso especial BLOQUE_CU_ITEMS: cada objeto debe tener esta estructura:
           {{
             ""CU_ID"": ""CU001"",
             ""CU_NOM"": ""Nombre del caso de uso"",
             ""CU_DESC"": ""Descripción técnica del caso de uso (3-5 líneas)"",
             ""CU_ACTORES"": ""Actor1; Actor2"",
             ""CU_PRE"": ""Precondiciones"",
             ""CU_FLOW_BASE"": ""1. ... 2. ... 3. ..."",
             ""CU_FLOW_ALT"": ""1. ... 2. ..."",
             ""CU_POST"": ""Postcondiciones"",
             ""CU_RESTRIC"": ""Restricciones"",
             ""CU_PADRE"": ""CU padre o Ninguno"",
             ""IMG_PROTOTIPO"": ""@startuml ... @enduml""
           }}
        5.1 Reglas para BLOQUE_CU_ITEMS:
           - CU_ID debe seguir el patrón CU### (ej. CU001, CU002).
           - CU_NOM y CU_ACTORES deben ser coherentes con el diagrama IMG_CU_GENERAL.
           - Cada caso de uso del diagrama debe tener su bloque en BLOQUE_CU_ITEMS.
        6. Caso especial BLOQUE_PQ_ITEMS: cada objeto debe tener:
           {{
             ""PQ_ID_NOM"": ""PQ001: Nombre del paquete"",
             ""PQ_DESC"": ""Descripción funcional/técnica del paquete alineada al diagrama"",
             ""PQ_CLASES_LISTA"": ""- ClaseA\n- ClaseB\n- ClaseC""
           }}
        7. Caso especial BLOQUE_ACT_ITEMS: cada objeto debe tener:
           {{
             ""CU_ID_ACT"": ""CU001"",
             ""CU_NOM_ACT"": ""Nombre del caso de uso"",
             ""CU_DESC_ACT"": ""Descripcion funcional del flujo de actividad (3-5 lineas)"",
             ""IMG_ACTIVIDAD"": ""@startuml ... @enduml""
           }}
        7.1 Reglas para IMG_ACTIVIDAD (segun contexto real):
           - Debe representar una secuencia completa de tareas del caso de uso (no una sola tarea aislada).
           - Si el contexto muestra varias etapas, modela 2 o mas fases (ej. Recepcion, Validacion, Resolucion/Salida).
           - Si el proyecto es pequeÃ±o o hay poca evidencia, permite un flujo general unico pero con varias tareas coherentes.
           - Usa entre 5 y 14 acciones reales del contexto, sin inventar tareas inexistentes.
           - Incluye decision (if/else) solo cuando aplique en el proceso.
           - Debe iniciar con start y terminar con stop, mostrando resultado final del flujo.
        8. Caso especial BLOQUE_SEQ_ITEMS: cada objeto debe tener:
           {{
             ""CU_ID_SEQ"": ""CU001"",
             ""CU_NOM_SEQ"": ""Nombre del caso de uso"",
             ""CU_DESC_SEQ"": ""Descripcion de la interaccion secuencial (3-5 lineas)"",
             ""IMG_SECUENCIA"": ""@startuml ... @enduml""
           }}
        8.1 Reglas para IMG_SECUENCIA (segun contexto real):
           - Si el contexto lo permite, representa 2 o mas fases del proceso (ej. Solicitud y Validacion/Respuesta).
           - Usa entre 2 y 5 participantes reales del contexto y entre 4 y 14 mensajes coherentes.
           - Los mensajes deben mostrar orden temporal completo de inicio a fin.
           - Incluye bloque de control (alt/opt/loop) solo cuando aplique.
           - Si el proyecto es pequeÃ±o, permite una secuencia general unica, pero completa y coherente.
        9. Caso especial BLOQUE_EST_ITEMS: cada objeto debe tener:
           {{
             ""CU_ID_EST"": ""CU001"",
             ""CU_NOM_EST"": ""Nombre del caso de uso"",
             ""CU_DESC_EST"": ""Descripcion de estados y transiciones (3-5 lineas)"",
             ""IMG_ESTADO"": ""@startuml ... @enduml""
           }}
        10. Caso especial BLOQUE_CAPAS_ITEMS: cada objeto debe tener:
           {{
             ""CAPA_NOM"": ""Nombre de la capa"",
             ""CAPA_DESC"": ""Descripción técnica de la capa""
           }}
        11. Caso especial BLOQUE_COMP_ITEMS: cada objeto debe tener:
            {{
              ""COMP_NOM"": ""Nombre del componente"",
              ""COMP_DESC"": ""Descripción técnica del componente""
            }}
        12. Caso especial BLOQUE_CLASE_DET_ITEMS: cada objeto debe tener:
            {{
              ""CLASE_TITULO"": ""Nombre de la clase"",
              ""CLASE_ATRIB"": ""atributo1:tipo; atributo2:tipo"",
              ""CLASE_OPER"": ""metodo1(); metodo2(param)"",
              ""CLASE_AGREG"": ""Agregación con otras clases"",
              ""CLASE_ASOC"": ""Asociaciones con otras clases""
            }}
        13. Regla de consistencia diagrama -> descripción:
            - Si existe IMG_ARQUITECTURA, también llena ARQ_DESC_GENERAL y BLOQUE_CAPAS_ITEMS.
            - Si existe IMG_COMPONENTES, también llena BLOQUE_COMP_ITEMS.
            - Si existe IMG_CLASES_SISTEMA, también llena BLOQUE_CLASE_DET_ITEMS.
            - Si existe IMG_VISTA_LOGICA, también llena PQ_VISTA_LOGICA_DESC y BLOQUE_PQ_ITEMS.
            - Para IMG_VISTA_LOGICA en PlantUML usa package en multilinea y flechas entre nodos internos, no entre nombres de package.
            - Si existe IMG_ACTORES, TABLA_ACTORES_LISTA debe listar actores y responsabilidades.
            - Si existe IMG_CU_GENERAL, TABLA_CU_LISTADO y BLOQUE_CU_ITEMS deben estar coherentes.
            - Si existe BLOQUE_ACT_ITEMS, cada item debe incluir CU_DESC_ACT.
            - Si existe BLOQUE_SEQ_ITEMS, cada item debe incluir CU_DESC_SEQ.
            - Si existe BLOQUE_EST_ITEMS, cada item debe incluir CU_DESC_EST.
        13.1 Regla visual obligatoria para IMG_CU_GENERAL (PlantUML):
            - Usa actores humanos a la izquierda y sistemas externos en el extremo derecho.
            - Encierra los casos de uso dentro de un límite de sistema al centro: rectangle ""NOMBRE_SISTEMA"" {{ ... }}.
            - Los casos de uso deben mostrarse como óvalos dentro del límite del sistema y nombrarse con prefijo CU###.
            - Conecta cada actor con sus casos de uso correspondientes.
            - Usa relaciones <<include>>/<<extend>> solo cuando aplique.
            - Estructura de referencia:
              @startuml
              left to right direction
              actor ""Actor Interno"" as A1
              actor ""Sistema Externo"" as EXT
              rectangle ""NOMBRE_SISTEMA"" {{
                usecase ""CU001: Caso 1"" as CU001
                usecase ""CU002: Caso 2"" as CU002
              }}
              A1 --> CU001
              EXT --> CU002
              CU002 .> CU001 : <<include>>
              @enduml
        13.2 Regla visual obligatoria para IMG_ACTORES (PlantUML):
            - No generar solo íconos de actores sueltos.
            - Debe incluir límite de sistema al centro con los casos de uso principales.
            - Actores internos a la izquierda y actor/sistema externo a la derecha cuando aplique.
            - Conecta actores con los casos de uso que ejecutan y refleja el flujo general.
        14. Si existe BLOQUE_CU_ITEMS en las claves, genera entre 1 y 9 casos de uso segun el contexto (proyecto pequeÃ±o: 1 o pocos; proyecto complejo: varios).
        15. Si existe BLOQUE_PQ_ITEMS, genera entre 1 y 10 paquetes segun la evidencia de la arquitectura.
        16. Si existe BLOQUE_ACT_ITEMS/BLOQUE_SEQ_ITEMS/BLOQUE_EST_ITEMS:
            - si hay varios casos de uso claros, genera la misma cantidad que BLOQUE_CU_ITEMS;
            - si hay un caso de uso general o poca evidencia, genera un bloque general coherente.
        17. Si existen BLOQUE_CAPAS_ITEMS/BLOQUE_COMP_ITEMS/BLOQUE_CLASE_DET_ITEMS, genera entre 1 y 8 elementos segun la arquitectura identificada.
        18. Cuando generes IMG_ACTIVIDAD, prioriza macro-actividades del proceso end-to-end y no pasos tÃ©cnicos microscÃ³picos.
        19. Regla general para cualquier diagrama (IMG_*/DIAGRAMA_*): no inventes actores, fases, componentes o relaciones que no esten sustentados en el contexto.
        20. Si faltan detalles, usa una representacion general y conservadora, manteniendo coherencia funcional y trazabilidad con las claves CU/PQ/ACT/SEQ/EST.
        21. Preferencia de granularidad: si no hay evidencia clara de multiples procesos/actores/fases, genera un solo diagrama general por tipo; solo divide en varios cuando el contexto lo justifique explícitamente.

        CLAVES REQUERIDAS (Genera el contenido óptimo para cada una):
        {jsonKeys}
        ";
    }

    public static string DocAnalyzeContext(string structure, string configFiles)
    {
        return $@"
        Analiza la siguiente estructura de proyecto de software y proporciona un resumen técnico conciso:
        
        ESTRUCTURA DE DIRECTORIOS:
        {structure}
        
        ARCHIVOS DE CONFIGURACIÓN:
        {configFiles}
        
        INSTRUCCIONES:
        Responde con un párrafo breve que incluya:
        - Tecnología/stack identificado.
        - Propósito probable del sistema.
        - Arquitectura aparente (Clean Architecture, Hexagonal, MVC, etc.).
        - Módulos/capas principales.
        Máximo 200 palabras.
        ";
    }
}
