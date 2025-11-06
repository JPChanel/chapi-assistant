namespace Chapi.Helper.AI;

public class GetPrompt
{
    public static string GitCommit(string request)
    {
        return $@"
        Analiza el siguiente 'diff' y genera un mensaje de commit profesional en español según el estándar Conventional Commits.

        👉 Reglas:
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

        🔧 Guía de Tipos:
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
}
