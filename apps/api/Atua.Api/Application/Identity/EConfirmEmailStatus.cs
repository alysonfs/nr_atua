namespace Atua.Api.Application.Identity;

public enum EConfirmEmailStatus
{
    Success,
    AlreadyConfirmed,
    InvalidCode,
    ExpiredCode
}
