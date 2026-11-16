namespace MenuPlanner.Domain;

/// <summary>Unidades de medida: métricas, caseras y de compra.</summary>
public enum Unit
{
    G,
    Ml,
    Pieza,
    Taza,
    Cda,
    Cdita,
    Porcion,
    Diente,
    Paquete,
    Lata,
}

/// <summary>Grupos de alimentos que componen un tiempo de comida del plan.</summary>
public enum ComponentGroup
{
    Verdura,
    Proteina,
    Leguminosa,
    Cereal,
    Grasa,
}

/// <summary>Estado en el que se mide una cantidad (el plan suele hablar de cocido; en el súper se compra crudo).</summary>
public enum FoodState
{
    Crudo,
    Cocido,
}

/// <summary>Punto del rango de proteína del plan que se usa para porcionar.</summary>
public enum ProteinTarget
{
    Min,
    Mid,
    Max,
}

public enum MenuStatus
{
    Draft,
    Confirmed,
}

/// <summary>Cómo participa la leguminosa en una receta.</summary>
public enum LegumeMode
{
    /// <summary>La receta no lleva leguminosa.</summary>
    None,

    /// <summary>La leguminosa es parte del guisado (garbanzos a la mexicana): va en todas las porciones.</summary>
    Included,

    /// <summary>Acompañamiento opcional (frijolitos): el menú decide en cuántas porciones se sirve.</summary>
    Side,
}

public static class Meals
{
    public const string Desayuno = "desayuno";
    public const string Colacion = "colacion";
    public const string Comida = "comida";
    public const string Snack = "snack";
    public const string Cena = "cena";
}
