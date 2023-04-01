using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Fido2NetLib.Objects;

namespace AspNetCoreFido2MFA.Models;

public class StoredCredential
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public byte[] UserId { get; set; }
    public byte[] PublicKey { get; set; }
    public byte[] UserHandle { get; set; }
    public uint SignatureCounter { get; set; }
    public string CredType { get; set; }
    public DateTime RegDate { get; set; }
    public Guid AaGuid { get; set; }

    [NotMapped]
    public PublicKeyCredentialDescriptor Descriptor {
        get => string.IsNullOrWhiteSpace(DescriptorJson) ? null : JsonSerializer.Deserialize<PublicKeyCredentialDescriptor>(DescriptorJson);
        set => DescriptorJson = JsonSerializer.Serialize(value);
    }

    public string DescriptorJson { get; set; }
}