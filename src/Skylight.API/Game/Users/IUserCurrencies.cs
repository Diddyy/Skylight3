namespace Skylight.API.Game.Users;

public interface IUserCurrencies
{
	decimal GetBalance(string currencyKey);
	void UpdateBalance(string currencyKey, decimal newBalance);
}
