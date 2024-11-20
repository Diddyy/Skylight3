using Skylight.API.Game.Users;

namespace Skylight.Server.Game.Users;

internal sealed class UserCurrencies(Dictionary<string, decimal> initialCurrencies) : IUserCurrencies
{
	private readonly Dictionary<string, decimal> currencies = new Dictionary<string, decimal>(initialCurrencies);

	public decimal GetBalance(string currencyKey)
	{
		return this.currencies.GetValueOrDefault(currencyKey, 0);
	}

	public void UpdateBalance(string currencyKey, decimal newBalance)
	{
		if (!this.currencies.TryAdd(currencyKey, newBalance))
		{
			this.currencies[currencyKey] = newBalance;
		}
	}
}
