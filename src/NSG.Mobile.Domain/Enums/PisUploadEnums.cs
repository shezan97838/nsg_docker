namespace NSG.Mobile.Domain.Enums;

/// <summary>Mode of a PIS Excel upload. Stored as int in <c>pis_upload_batches.upload_type</c>.</summary>
public enum PisUploadType
{
    /// <summary>Insert new personnel and update existing ones, matched by NSG number.</summary>
    Upsert = 1,

    /// <summary>Update existing personnel only; an unknown NSG number is rejected and never inserted.</summary>
    UpdateOnly = 2
}

/// <summary>Lifecycle of a PIS upload batch. Stored as int.</summary>
public enum PisUploadStatus
{
    Processing = 1,

    /// <summary>Every row was inserted or updated.</summary>
    Completed = 2,

    /// <summary>The file was processed; at least one row was rejected (the others were saved).</summary>
    CompletedWithErrors = 3,

    /// <summary>The whole file was refused (wrong format, wrong headings, too large). Nothing was saved.</summary>
    Rejected = 4,

    /// <summary>Processing stopped unexpectedly. Rows saved before the stop remain saved and are listed under the batch.</summary>
    Failed = 5
}

/// <summary>What a successful row did to the personnel record. Stored as int.</summary>
public enum PisRowOperation
{
    Inserted = 1,
    Updated = 2
}

/// <summary>Why a row was rejected: a validation/processing failure or a conflict. Stored as int.</summary>
public enum PisRowStatus
{
    Failed = 1,
    Conflict = 2
}
