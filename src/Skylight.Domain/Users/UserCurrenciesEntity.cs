namespace Skylight.Domain.Users;

public class UserCurrenciesEntity
{
	public int UserId { get; init; }
	public UserEntity? User { get; set; }

	public string Currency { get; set; } = null!;
	public decimal Balance { get; set; }
}
