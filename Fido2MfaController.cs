using AspNetCoreFido2MFA.Data;
using AspNetCoreFido2MFA.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AspNetCoreFido2MFA;

[Route("api/[controller]")]
public class Fido2MfaController : Controller
{
    private readonly IFido2 _fido2;
    private readonly AppDbContext _context;

    public Fido2MfaController(IFido2 fido2, AppDbContext context)
    {
        _fido2 = fido2;
        _context = context;
    }

    private string FormatException(Exception e)
    {
        return $"{e.Message}{(e.InnerException != null ? " (" + e.InnerException.Message + ")" : "")}";
    }

    [HttpPost]
    [Route("/makeCredentialOptions")]
    public async Task<JsonResult> MakeCredentialOptions([FromForm] string username,
        [FromForm] string displayName,
        [FromForm] string attType,
        [FromForm] string authType,
        [FromForm] string userVerification,
        [FromQuery] bool requireResidentKey)
    {
        try
        {
            // 1. Get user from DB by username (in our example, auto create missing users)
            var user = await _context.Fido2Users.SingleOrDefaultAsync(u => u.Name.Equals(username));

            if (user is null)
                throw new ArgumentException("User not found");

            // 2. Get user existing keys by username
            var existingKeys = await _context.StoredCredentials
                .Where(c => c.UserId.SequenceEqual(user.Id))
                .Select(c => c.Descriptor)
                .ToListAsync();

            // 3. Create options
            var authenticatorSelection = new AuthenticatorSelection
            {
                RequireResidentKey = requireResidentKey,
                UserVerification = userVerification.ToEnum<UserVerificationRequirement>()
            };

            if (!string.IsNullOrEmpty(authType))
                authenticatorSelection.AuthenticatorAttachment = authType.ToEnum<AuthenticatorAttachment>();

            var extensions = new AuthenticationExtensionsClientInputs()
            {
                Extensions = true,
                UserVerificationMethod = true,
            };

            var options = _fido2.RequestNewCredential(user, existingKeys, authenticatorSelection,
                attType.ToEnum<AttestationConveyancePreference>(), extensions);

            // 4. Temporarily store options, session/in-memory cache/redis/db
            HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

            // 5. return options to client
            return Json(options);
        }
        catch (Exception e)
        {
            return Json(new CredentialCreateOptions {Status = "error", ErrorMessage = FormatException(e)});
        }
    }

    [HttpPost]
    [Route("/makeCredential")]
    public async Task<JsonResult> MakeCredential([FromBody] AuthenticatorAttestationRawResponse attestationResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. get the options we sent the client
            var jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
            var options = CredentialCreateOptions.FromJson(jsonOptions);

            // 2. Create callback so that lib can verify credential id is unique to this user
            async Task<bool> Callback(IsCredentialIdUniqueToUserParams args, CancellationToken token)
            {
                var credentialIdString = Base64Url.Encode(args.CredentialId);

                var cred = await _context.StoredCredentials
                    .Where(c => c.DescriptorJson.Contains(credentialIdString))
                    .FirstOrDefaultAsync(token);

                if (cred is null)
                    return true;
                
                var users = await _context.Fido2Users
                    .Where(u => u.Id.SequenceEqual(cred.UserId))
                    .ToListAsync(token);

                return users.Count <= 0;
            }

            // 2. Verify and make the credentials
            var success = await _fido2.MakeNewCredentialAsync(attestationResponse, options, Callback,
                cancellationToken: cancellationToken);

            // 3. Store the credentials in db
            if (success.Result != null)
            {
                var credential = new StoredCredential
                {
                    Descriptor = new PublicKeyCredentialDescriptor(success.Result.CredentialId),
                    PublicKey = success.Result.PublicKey,
                    UserHandle = success.Result.User.Id,
                    SignatureCounter = success.Result.Counter,
                    CredType = success.Result.CredType,
                    RegDate = DateTime.Now,
                    AaGuid = success.Result.Aaguid,
                    UserId = options.User.Id,
                    UserName = options.User.Name,
                };

                await _context.StoredCredentials.AddAsync(credential, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            // 4. return "ok" to the client
            return Json(success);
        }
        catch (Exception e)
        {
            return Json(new Fido2.CredentialMakeResult(status: "error", errorMessage: FormatException(e),
                result: null));
        }
    }

    [HttpPost]
    [Route("/assertionOptions")]
    public async Task<ActionResult> AssertionOptionsPost([FromForm] string username, [FromForm] string userVerification)
    {
        try
        {
            var existingCredentials = new List<PublicKeyCredentialDescriptor>();

            if (!string.IsNullOrEmpty(username))
            {
                // 1. Get user from DB
                var user = await _context.Fido2Users.SingleOrDefaultAsync(u => u.Name.Equals(username));

                if (user is null) throw new ArgumentException("Username was not registered");

                // 2. Get registered credentials from database
                existingCredentials = await _context.StoredCredentials
                    .Where(c => c.UserName.Equals(username))
                    .Select(c => c.Descriptor)
                    .ToListAsync();
            }

            var extensions = new AuthenticationExtensionsClientInputs()
            {
                UserVerificationMethod = true
            };

            // 3. Create options
            var uv = string.IsNullOrEmpty(userVerification)
                ? UserVerificationRequirement.Discouraged
                : userVerification.ToEnum<UserVerificationRequirement>();

            var options = _fido2.GetAssertionOptions(
                existingCredentials,
                uv,
                extensions
            );

            // 4. Temporarily store options, session/in-memory cache/redis/db
            HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());

            // 5. Return options to client
            return Json(options);
        }

        catch (Exception e)
        {
            return Json(new AssertionOptions {Status = "error", ErrorMessage = FormatException(e)});
        }
    }

    [HttpPost]
    [Route("/makeAssertion")]
    public async Task<JsonResult> MakeAssertion([FromBody] AuthenticatorAssertionRawResponse clientResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get the assertion options we sent the client
            var jsonOptions = HttpContext.Session.GetString("fido2.assertionOptions");
            var options = AssertionOptions.FromJson(jsonOptions);

            // 2. Get registered credential from database
            var credentialIdString = Base64Url.Encode(clientResponse.Id);

            var credential = await _context.StoredCredentials
                .Where(c => c.DescriptorJson.Contains(credentialIdString)).FirstOrDefaultAsync(cancellationToken);

            if (credential is null) throw new Exception("Unknown credentials");

            // 3. Get credential counter from database
            var storedCounter = credential.SignatureCounter;

            // 4. Create callback to check if userhandle owns the credentialId
            async Task<bool> Callback(IsUserHandleOwnerOfCredentialIdParams args, CancellationToken token)
            {
                var storedCredentials = await _context.StoredCredentials
                    .Where(c => c.UserHandle.SequenceEqual(args.UserHandle)).ToListAsync(cancellationToken);
                return storedCredentials.Exists(c => c.Descriptor.Id.SequenceEqual(args.CredentialId));
            }

            // 5. Make the assertion
            var res = await _fido2.MakeAssertionAsync(clientResponse, options, credential.PublicKey, storedCounter,
                Callback,
                cancellationToken: cancellationToken);

            // 6. Store the updated counter
            var cred = await _context.StoredCredentials
                .Where(c => c.DescriptorJson.Contains(credentialIdString)).FirstOrDefaultAsync(cancellationToken);

            cred.SignatureCounter = res.Counter;

            await _context.SaveChangesAsync(cancellationToken);

            // 7. return OK to client
            return Json(res);
        }
        catch (Exception e)
        {
            return Json(new AssertionVerificationResult {Status = "error", ErrorMessage = FormatException(e)});
        }
    }
}