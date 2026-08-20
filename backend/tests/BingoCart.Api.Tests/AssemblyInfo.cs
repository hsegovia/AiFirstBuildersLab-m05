using Xunit;

// Block 2 del spec FEAT-005: GET /api/organizadores/directorio agrega datos de TODOS los
// organizadores (a diferencia de POST/GET /api/bingos, que siempre filtra por organizadorId) — el
// test que valida AC-03 end-to-end (`Directorio_SinNingunOrganizadorConBingoActivo_...`) exige que
// la base no tenga NINGÚN bingo activo en el momento de la consulta. xUnit corre las clases de test
// de este proyecto (cada una es su propia collection implícita) en paralelo por defecto: sin
// deshabilitar esa paralelización, `OrganizadoresControllerTests` puede leer bingos activos
// sembrados en el mismo instante por `BingosControllerTests` contra la MISMA base SQL Server
// compartida (`BingoCart`), y el test de "base limpia" falla de forma intermitente aunque el
// endpoint sea correcto. Ningún test individual necesitaba esta garantía antes de este bloque
// (todos filtraban por organizadorId propio); el directorio es el primer endpoint cuyo resultado
// depende del estado GLOBAL de la base.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
