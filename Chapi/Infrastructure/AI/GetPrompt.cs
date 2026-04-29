namespace Chapi.Infrastructure.AI;

public class GetPrompt
{
    public static string GitCommit(string request)
    {
        return $@"
        Analiza el siguiente 'diff' y genera un mensaje de commit profesional en espanol segun el estandar Conventional Commits.

        ?? Reglas:
        - Debes retornar SOLO un objeto JSON valido, sin formato markdown (```json).
        - El JSON debe tener esta estructura:
          {{
            ""summary"": ""tipo(alcance): resumen corto"",
            ""description"": ""descripcion detallada de los cambios""
          }}
        - 'summary' (Resumen): Una sola linea, 72 caracteres maximo.
        - 'description' (Descripcion):
            - Un resumen de 1 o 2 frases sobre el 'por que' del cambio.
            - Seguido de una lista de viñetas (usando '-') con los cambios mas importantes.
            - Si los cambios son muy peque?os, la descripcion puede ser un string vacio ("""").

        ?? Guia de Tipos:
        - 'feat': Nuevas funciones.
        - 'fix': Correccion de errores.
        - 'refactor': Limpieza de codigo sin cambiar funcionalidad.
        - 'chore': Tareas de mantenimiento, builds, etc.

        Ejemplo de Salida JSON:
        {{
          ""summary"": ""feat(git): implementa generacipn de commit con IA y manejo de diff"",
          ""description"": ""Se actualiza el flujo de commits para usar la IA.\n\n- Modifica btnGitCommit_Click para analizar solo archivos seleccionados.\n- Cambia el comando diff a 'diff HEAD' para evitar el stage.\n- Añade deserializaci?n para el nuevo formato JSON de respuesta.""
        }}

        Texto del diff (Contexto):
        {request}
        ";

    }
    public static string AnalyzeEmail(string moduleName, string methodName, string emailContent, string dataBase, string tipoMetodo)
    {
        return $@"
            Analiza el siguiente correo tecnico o procedimiento Almacenado y extrae la informacion del Stored Procedure.

            CONTEXTO:
            - Modulo: {moduleName}
            - Metodo: {methodName}
            - Tipo Metodo: {tipoMetodo}

            CORREO TECNICO:
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
            2. RequestParameters: par?metros de entrada (tipo nombre) equivalente en ingles en camelCase, si no se puede ver el tipo de dato infiere y ponle el tipo; siempre debes darme estandar de .netCore en ingles ""public int parametro {{ get; set; }}""
            3. Parameters: mapea el requestParameter a lo q espera el SP y hace su mapeo automatico ; ahora si la base de datos es POSTGRES y es un Post,Put o Delete  el Parameter q retorne en este formato ejm: =>(
            parameters.Add(""@an_cod_rol"", datos.n_cod_rol);
            parameters.Add(""@as_des_rol"", datos.s_des_rol);
            parameters.Add(""@as_accion"", datos.s_accion);
            parameters.Add(""@rn_codigo"", dbType: DbType.Int32, direction: ParameterDirection.Output);
            parameters.Add(""@rs_valor"", dbType: DbType.String, direction: ParameterDirection.Output, size: 100);
            )
            4. DtoFields: campos que retorna la BD (tipo nombreCampo) todo en minusculas tal cual el valor de retorno de la bd, si no se puede ver el tipo de dato infiere y ponle el tipo;  siempre debes darme estandar de .netCore ""public string parametro {{ get; set; }}""
            5. ResponseMapper: mapeo para objeto an?nimo en ingles camelCase (propiedadAPI = dto.campo)
            6. Usa tipos C#: int, string, decimal, DateTime, bool            
            7. NO inventes datos, solo extrae lo del correo
            8. Si no hay par?metros, retorna array vacio []

            EST?NDARES DE NOMBRADO PERSONALIZADOS:
            - Los nombres en ingl?s deben seguir tus convenciones:
              ? Todo campo que represente un C?DIGO termina con **Code** (ej: resolutionCode, districtOriginCode, expedientCode).
              ? Todo campo que represente un NOMBRE o DESCRIPCI?N termina con **Name** (ej: resolutionName, magistrateName).
              ? Todo campo que represente un FLA  inician con **fla** (ej: flaSigned, flaProcess).
              ? Usa camelCase en todos los nombres.
              ? Ejemplos de propiedades estandar que debes seguir:
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
            Adecua los nombres generados a estos estandares si los terminos del correo tecnico son equivalentes o cercanos.";

    }

    public static string GenerateSqlCall(string procedureName, string dbType, string netParams)
    {
        string syntaxHelp = "";
        string exampleCall = "";

        switch (dbType)
        {
            case "Postgres (Funci?n)":
                syntaxHelp = "La sintaxis debe ser una consulta SELECT: SELECT * FROM schema.mi_funcion(param1 => valor1, param2 => valor2);";
                exampleCall = "SELECT * FROM mi_schema.fn_buscar_usuario(an_id_usuario => 123, as_nombre => 'juan');";
                break;
            case "Postgres (SP)":
                syntaxHelp = "La sintaxis debe ser una llamada CALL: CALL schema.mi_sp(param1 => valor1, param2 => valor2);";
                exampleCall = "CALL mi_schema.sp_actualizar_stock(an_id_producto => 99, an_cantidad => 50);";
                break;
            case "Sybase (SP)":
                syntaxHelp = "La sintaxis debe ser una llamada CALL con par?metros nombrados: CALL \"schema\".\"mi_sp\"(\"param1\" = valor1, \"param2\" = valor2);";
                exampleCall = "CALL \"OCMAERP\".\"SP_SANCVAL_MANT\"(\"AN_COD_INTEXP\" = 790549, \"AS_NRO_RESO\" = '004', \"AD_FEC_MOVIMI\" = '2025-11-03');";
                break;
            default:
                syntaxHelp = "La sintaxis debe ser una llamada CALL con par?metros nombrados: CALL \"schema\".\"mi_sp\"(\"param1\" = valor1, \"param2\" = valor2);";
                exampleCall = "CALL \"OCMAERP\".\"SP_SANCVAL_MANT\"(\"AN_COD_INTEXP\" = 790549, \"AS_NRO_RESO\" = '004', \"AD_FEC_MOVIMI\" = '2025-11-03');";
                break;
        }

        return $"""
        Eres un asistente experto en SQL. Tu tarea es convertir una cadena de par?metros de .NET en una consulta SQL ejecutable para depuraci?n.

        **Tarea:**
        1.  Recibir?s un nombre de Procedimiento/Funci?n, un tipo de BD y una lista de par?metros de .NET.
        2.  Debes generar el comando SQL para ejecutarlo.

        **Reglas Estrictas de Formateo:**
        1.  **Detecci?n de Tipos:**
            - Si un valor es puramente num?rico (ej: `50`, `0`, `790549`), tratalo como un N?MERO (sin comillas).
            - Si un valor es CUALQUIER OTRA COSA (ej: `004`, `DSDFSDSSFSD`, `24196~1...`, `LCORDOVA`, `U`), tratalo como un STRING (con comillas simples: `'valor'`).
            - Si un valor esta vacio (ej: `AS_DOCUMENTOS = ,`), tratalo como un string vacio (`''`).
            - Si un valor es una fecha/hora .NET (ej: `3/11/2025 00:00:00`), convi?rtelo a formato `YYYY-MM-DD` como un string (ej: `'2025-11-03'`).
        2.  **Sintaxis SQL:**
            - Sigue la sintaxis espec?fica para el tipo de BD.
            - {syntaxHelp}
            - Ejemplo de sintaxis: {exampleCall}
        3.  **Formato de Salida:**
            - Devuelve UNICAMENTE el bloque de codigo SQL.
            - No incluyas "Respuesta:", "Aque esta el SQL:", ni ` ```sql `.
            - Formatea el SQL con saltos de linea para que sea legible, como en el ejemplo del usuario.

        **Datos de Entrada:**
        -   **Procedimiento/Funcion:** `{procedureName}`
        -   **Tipo de BD:** `{dbType}`
        -   **Parametros .NET:**
            ```
            {netParams}
            ```

        **Salida (Solo SQL):**
        """;
    }

    public static string ChatAssistant(string contextInfo, string conversationHistory, string capabilitiesInfo, string userMessage)
    {
        return $@"
                Eres un asistente de desarrollo integrado en Chapi Assistant, una aplicacion para gestion de proyectos y Git.

                TU PERSONALIDAD:
                - Hablas en espanol de forma natural y amigable
                - Eres experto en desarrollo de software, Git, arquitectura y buenas practicas

                TUS CAPACIDADES EN ESTE PROYECTO (CHAPI):
                {capabilitiesInfo}

                REGLAS DE ACCI?N:
                1. Si el usuario te pide hacer algo que esta dentro de tus capacidades (como un commit), debes sugerir la accion si solo tienes todo claro y preparado; ello despues de explicar que har?s.
                  
                  **VALIDACION CRITICA DE PROYECTO**:
                  Si en el contexto ves '?? No hay proyecto seleccionado actualmente':
                  - NO puedes ejecutar acciones de Git (commit, push, pull, branch, etc.).
                  - S? puedes ejecutar acciones de Gesti?n de Proyecto (project.create, project.clone, project.list, project.add).
                  - Si el usuario pide algo de Git y no hay proyecto, dile amablemente: 'Primero debes abrir o crear un proyecto para hacer eso.'
                2. Para sugerir una accion, incluye AL FINAL de tu respuesta (despues de tu explicaci?n) un bloque con este formato exacto:
                  [[ACTION:{{""type"":""ID_EXACTO_DE_LA_CAPACIDAD"",""params"":{{""param1"":""valor1""}}}}]]
                  
                  IMPORTANTE: Usa el ID EXACTO listado en ""TUS CAPACIDADES"" y que sea mas adecuado (ej: git.commit, project.create, project.clone). NO INVENTES NOMBRES DE ACCION.

                3. REGLAS ESPEC?FICAS DE ACCIONES:
                  - Para COMMIT (git.commit): Siempre genera un mensaje profesional Conventional Commits.
                    Si el usuario pide ""commit y subir"" o ""hacer push despues"", agrega el par?metro ""push"": ""true"".
                    Ej: [[ACTION:{{""type"":""git.commit"",""params"":{{""message"":""feat(ui): nuevos botones"",""push"":""true""}}}}]]
                    Si solo pide commit:
                    Ej: [[ACTION:{{""type"":""git.commit"",""params"":{{""message"":""feat(core): update logic""}}}}]]
                  
                  - Para CREAR PROYECTO (project.create):
                    **USO EXCLUSIVO**: ?salo SOLO si el usuario pide expl?citamente crear una 'API', 'Backend', 'Proyecto .NET', 'Arquitectura Hexagonal' o 'Clean Architecture'.
                    Debes extraer **nombre** y **ruta**.
                    Ej: [[ACTION:{{""type"":""project.create"",""params"":{{""name"":""MiApiHexagonal"",""path"":""C:\\Ubicacion""}}}}]]
                    
                    **DESAMBIGUACI?N IMPORTANTE**: 
                    Si el usuario solo dice 'crear un proyecto' (sin especificar tipo), NO sugieras ninguna accion todav?a.
                    Preguntale: '?Quieres clonar un repositorio existente o crear una nueva API con Clean Architecture?'

                    Si falta el nombre O la ruta, PREG?NTALE al usuario. NO uses valores por defecto.

                  - Para CLONAR PROYECTO (project.clone):
                    Debes extraer **URL** y **ruta**.
                    Ej: [[ACTION:{{""type"":""project.clone"",""params"":{{""url"":""https://github.com/u/repo.git"",""path"":""C:\\Ubicacion""}}}}]]
                    Si falta la URL o la ruta, PREG?NTALE al usuario.
                4. IMPORTANTE: S?lo sugiere la accion si el usuario la pidi? o es el siguiente paso l?gico. No realices acciones sin preguntar si el usuario no fue expl?cito.

                {contextInfo}

                {conversationHistory}

                === MENSAJE ACTUAL DEL USUARIO ===
                {userMessage}
                ";
    }
    public static string DocSection(string sectionTitle, string projectContext)
    {
        return $@"
        Eres un experto en documentaci?n de software. Redacta el contenido de la secci?n ""{sectionTitle}"" 
        para un documento tecnico de ingenier?a de software.
        
        CONTEXTO DEL PROYECTO:
        {projectContext}

        REGLAS:
        - Responde SOLO con el contenido en Markdown bien estructurado.
        - NO incluyas encabezados H1 (titulo principal).
        - Se tecnico, conciso y profesional. 
        - Maximo 300 palabras.
        ";
    }

    public static string DocDiagram(string sectionTitle, string format, string projectContext)
    {
        var formatInstructions = format.ToLower() == "mermaid"
            ? "Usa la sintaxis Mermaid correcta. Empieza con el tipo: classDiagram, sequenceDiagram, graph LR, erDiagram, etc."
            : "Usa la sintaxis PlantUML correcta. Incluye @startuml y @enduml.";

        return $@"
        Genera c?digo {format} para el diagrama de ""{sectionTitle}"" basado en este contexto de proyecto:
        
        CONTEXTO:
        {projectContext}

        REGLAS ESTRICTAS:
        1. Responde SOLO con el codigo {format}, sin explicacion, sin bloques de markdown (```).
        2. {formatInstructions}
        3. El código debe ser valido y renderizable.
        4. Usa nombres reales del proyecto si estan disponibles en el contexto.
        5. Manten el diagrama enfocado y claro, maximo 15 elementos.
        ";
    }

    public static string DocMetadata(string jsonKeys, string projectContext, string userPrompt)
    {
        return $@"
        Eres un arquitecto de software experto documentando sistemas.
        Completa una plantilla JSON usando solo el contexto del proyecto y la instruccion del usuario.

        CONTEXTO:
        {projectContext}

        INSTRUCCION:
        {userPrompt}

        REGLAS GENERALES:
        1. Devuelve UNICAMENTE un objeto JSON valido, sin markdown.
        2. Incluye EXACTAMENTE las claves solicitadas; no agregues ni elimines claves.
        3. Para claves narrativas, no dejes valores vacios; si falta evidencia, responde de forma conservadora y coherente con el contexto.
        4. Para claves que contengan IMG o DIAGRAMA, devuelve codigo PlantUML valido sin bloques ```.
        5. Para claves que terminen en _ITEMS, devuelve un JSON ARRAY de objetos, no string.
        6. No inventes actores, procesos, componentes, tablas, paquetes, procedimientos, vistas, funciones ni indices.
        7. Si existe [DB_OBJECTS], usalo como fuente principal para objetos de base de datos.

        FORMATO POR TIPO:
        - INTRODUCCION: 2 parrafos formales, 120-220 palabras en total, con contexto institucional, objetivo del documento y valor tecnico.
        - OBJETIVOS: 4 a 7 objetivos accionables en lista con vinetas.
        - ALCANCE: 2 parrafos formales, 120-220 palabras, con alcance funcional, tecnico, integraciones, restricciones y ambito.
        - TABLA_DICC_RESUMEN: lista todas las tablas detectadas con formato ""tabla_a; tabla_b; tabla_c""; si no hay evidencia, ""No identificado en contexto"".
        - TABLA_OBJ_PQ/TABLA_OBJ_PROC/TABLA_OBJ_VISTAS/TABLA_OBJ_FUNC/TABLA_OBJ_IDX: usa solo objetos reales del contexto; si no hay evidencia, ""No identificado en contexto"".

        ESTRUCTURAS ESPECIALES:
        - BLOQUE_CU_ITEMS:
          {{
            ""CU_ID"": ""CU001"",
            ""CU_NOM"": ""Nombre del caso de uso"",
            ""CU_DESC"": ""Descripcion tecnica del caso de uso (3-5 lineas)"",
            ""CU_ACTORES"": ""Actor1; Actor2"",
            ""CU_PRE"": ""Precondiciones"",
            ""CU_FLOW_BASE"": ""1. ... 2. ... 3. ..."",
            ""CU_FLOW_ALT"": ""1. ... 2. ..."",
            ""CU_POST"": ""Postcondiciones"",
            ""CU_RESTRIC"": ""Restricciones"",
            ""CU_PADRE"": ""CU padre o Ninguno"",
            ""IMG_PROTOTIPO"": ""@startuml ... @enduml""
          }}
          Reglas: CU_ID sigue CU###; CU_NOM y CU_ACTORES deben ser coherentes con IMG_CU_GENERAL; genera entre 1 y 9 casos segun la evidencia.
        - BLOQUE_PQ_ITEMS:
          {{
            ""PQ_ID_NOM"": ""PQ001: Nombre del paquete"",
            ""PQ_DESC"": ""Descripcion funcional/tecnica del paquete alineada al diagrama"",
            ""PQ_CLASES_LISTA"": ""- ClaseA\n- ClaseB\n- ClaseC""
          }}
          Genera entre 1 y 10 paquetes segun la arquitectura identificada.
        - BLOQUE_ACT_ITEMS:
          {{
            ""CU_ID_ACT"": ""CU001"",
            ""CU_NOM_ACT"": ""Nombre del caso de uso"",
            ""CU_DESC_ACT"": ""Descripcion funcional del flujo de actividad (3-5 lineas)"",
            ""IMG_ACTIVIDAD"": ""@startuml ... @enduml""
          }}
        - BLOQUE_SEQ_ITEMS:
          {{
            ""CU_ID_SEQ"": ""CU001"",
            ""CU_NOM_SEQ"": ""Nombre del caso de uso"",
            ""CU_DESC_SEQ"": ""Descripcion de la interaccion secuencial (3-5 lineas)"",
            ""IMG_SECUENCIA"": ""@startuml ... @enduml""
          }}
        - BLOQUE_EST_ITEMS:
          {{
            ""CU_ID_EST"": ""CU001"",
            ""CU_NOM_EST"": ""Nombre del caso de uso"",
            ""CU_DESC_EST"": ""Descripcion de estados y transiciones (3-5 lineas)"",
            ""IMG_ESTADO"": ""@startuml ... @enduml""
          }}
        - BLOQUE_CAPAS_ITEMS:
          {{
            ""CAPA_NOM"": ""Nombre de la capa"",
            ""CAPA_DESC"": ""Descripcion tecnica de la capa""
          }}
        - BLOQUE_COMP_ITEMS:
          {{
            ""COMP_NOM"": ""Nombre del componente"",
            ""COMP_DESC"": ""Descripcion tecnica del componente""
          }}
        - BLOQUE_CLASE_DET_ITEMS:
          {{
            ""CLASE_TITULO"": ""Nombre de la clase"",
            ""CLASE_ATRIB"": ""atributo1:tipo; atributo2:tipo"",
            ""CLASE_OPER"": ""metodo1(); metodo2(param)"",
            ""CLASE_AGREG"": ""Agregacion con otras clases"",
            ""CLASE_ASOC"": ""Asociaciones con otras clases""
          }}
        - BLOQUE_DICC_TABLA_ITEMS: devuelve un array; cada objeto debe tener EXACTAMENTE
          {{
            ""DICC_TABLA_TITULO"": ""Nombre de tabla"",
            ""COL_NOM"": ""columna1\ncolumna2\ncolumna3"",
            ""COL_TIPO"": ""tipo1\ntipo2\ntipo3"",
            ""COL_PK"": ""SI\nNO\nNO"",
            ""COL_DESC"": ""descripcion1\ndescripcion2\ndescripcion3""
          }}
          Genera un objeto por cada tabla confirmada en contexto; no inventes tablas.

        CONSISTENCIA ENTRE CLAVES:
        - Si existe IMG_ARQUITECTURA, tambien llena ARQ_DESC_GENERAL y BLOQUE_CAPAS_ITEMS.
        - Si existe IMG_COMPONENTES, tambien llena BLOQUE_COMP_ITEMS.
        - Si existe IMG_CLASES_SISTEMA, tambien llena BLOQUE_CLASE_DET_ITEMS.
        - Si existe IMG_VISTA_LOGICA, tambien llena PQ_VISTA_LOGICA_DESC y BLOQUE_PQ_ITEMS.
        - Si existe IMG_ACTORES, TABLA_ACTORES_LISTA debe listar actores y responsabilidades.
        - Si existe IMG_CU_GENERAL, TABLA_CU_LISTADO y BLOQUE_CU_ITEMS deben ser coherentes.
        - Si existe BLOQUE_ACT_ITEMS/BLOQUE_SEQ_ITEMS/BLOQUE_EST_ITEMS, cada item debe incluir su campo descriptivo correspondiente.

        REGLAS DE DIAGRAMAS:
        - Usa solo elementos sustentados en el contexto.
        - Si falta detalle, genera un diagrama general y conservador.
        - IMG_ACTIVIDAD: flujo end-to-end, 5-14 acciones reales, puede incluir decisiones, inicia con start y termina con stop.
        - IMG_SECUENCIA: 2-5 participantes reales, 4-14 mensajes, orden temporal completo, usa alt/opt/loop solo si aplica.
        - IMG_CU_GENERAL: actores humanos a la izquierda, sistemas externos a la derecha, casos de uso dentro de rectangle ""NOMBRE_SISTEMA"", nombres con prefijo CU###.
        - IMG_ACTORES: no dejes actores sueltos; incluye limite de sistema y relacion con casos de uso principales.
        - IMG_VISTA_LOGICA: usa package en multilinea y relaciones entre nodos internos, no entre nombres de package.

        CLAVES REQUERIDAS:
        {jsonKeys}
        ";
    }

    public static string DocAnalyzeContext(string structure, string configFiles)
    {
        return $@"
        Analiza la siguiente estructura de proyecto de software y proporciona un resumen tecnico conciso:
        
        ESTRUCTURA DE DIRECTORIOS:
        {structure}
        
        ARCHIVOS DE CONFIGURACI?N:
        {configFiles}
        
        INSTRUCCIONES:
        Responde con un p?rrafo breve que incluya:
        - Tecnologia/stack identificado.
        - Proposito probable del sistema.
        - Arquitectura aparente (Clean Architecture, Hexagonal, MVC, etc.).
        - Modulos/capas principales.
        Maximo 200 palabras.
        ";
    }
}
