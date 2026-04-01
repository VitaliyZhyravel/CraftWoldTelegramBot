using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Util;
using Nethereum.Web3;
using WebApplication1.Contracts;
using WebApplication1.Models;
using WebApplication1.Options;

namespace WebApplication1.Services;

public sealed class RoninPoolPriceService : IRoninPoolPriceService
{
    private static readonly BigInteger Q192 = BigInteger.Pow(2, 192);
    private readonly ConcurrentDictionary<string, Task<PoolMetadata>> _poolMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<RoninPoolPriceService> _logger;
    private readonly Web3 _web3;

    public RoninPoolPriceService(
        IOptions<RoninPoolOptions> options,
        ILogger<RoninPoolPriceService> logger)
    {
        var rpcEndpoint = options.Value.RpcEndpoint;
        if (string.IsNullOrWhiteSpace(rpcEndpoint))
        {
            throw new ArgumentException("Ronin RPC endpoint is not configured.", nameof(options));
        }

        _logger = logger;
        _web3 = new Web3(rpcEndpoint);
    }

    public async Task<PoolPriceResult> GetPoolPriceAsync(string poolAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(poolAddress))
        {
            throw new ArgumentException("Pool address must be provided.", nameof(poolAddress));
        }

        var normalizedPoolAddress = NormalizeAddress(poolAddress);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slot0Handler = _web3.Eth.GetContractQueryHandler<Slot0Function>();
            var slot0Task = slot0Handler
                .QueryDeserializingToObjectAsync<Slot0OutputDto>(new Slot0Function(), normalizedPoolAddress);

            var metadata = await GetPoolMetadataAsync(normalizedPoolAddress);
            var slot0 = await slot0Task;

            var price = CalculatePrice(slot0.SqrtPriceX96, metadata.Token0Decimals, metadata.Token1Decimals);
            var inversePrice = price == 0m ? 0m : 1m / price;

            return new PoolPriceResult
            {
                PoolAddress = normalizedPoolAddress,
                Token0Symbol = metadata.Token0Symbol,
                Token1Symbol = metadata.Token1Symbol,
                Token0Address = metadata.Token0Address,
                Token1Address = metadata.Token1Address,
                Price = price,
                InversePrice = inversePrice,
                Tick = slot0.Tick
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read Ronin pool price for {PoolAddress}", normalizedPoolAddress);
            _poolMetadataCache.TryRemove(normalizedPoolAddress, out _);
            throw;
        }
    }

    private async Task<PoolMetadata> GetPoolMetadataAsync(string poolAddress)
    {
        try
        {
            return await _poolMetadataCache.GetOrAdd(poolAddress, LoadPoolMetadataAsync);
        }
        catch
        {
            _poolMetadataCache.TryRemove(poolAddress, out _);
            throw;
        }
    }

    private async Task<PoolMetadata> LoadPoolMetadataAsync(string poolAddress)
    {
        var token0Handler = _web3.Eth.GetContractQueryHandler<Token0Function>();
        var token1Handler = _web3.Eth.GetContractQueryHandler<Token1Function>();

        var token0AddressTask = token0Handler.QueryAsync<string>(poolAddress, new Token0Function());
        var token1AddressTask = token1Handler.QueryAsync<string>(poolAddress, new Token1Function());

        await Task.WhenAll(token0AddressTask, token1AddressTask);

        var token0Address = NormalizeAddress(await token0AddressTask);
        var token1Address = NormalizeAddress(await token1AddressTask);

        var token0MetadataTask = GetTokenMetadataAsync(token0Address);
        var token1MetadataTask = GetTokenMetadataAsync(token1Address);

        await Task.WhenAll(token0MetadataTask, token1MetadataTask);

        var token0Metadata = await token0MetadataTask;
        var token1Metadata = await token1MetadataTask;

        return new PoolMetadata
        {
            Token0Address = token0Address,
            Token1Address = token1Address,
            Token0Decimals = token0Metadata.Decimals,
            Token1Decimals = token1Metadata.Decimals,
            Token0Symbol = token0Metadata.Symbol,
            Token1Symbol = token1Metadata.Symbol
        };
    }

    private async Task<TokenMetadata> GetTokenMetadataAsync(string tokenAddress)
    {
        var decimalsHandler = _web3.Eth.GetContractQueryHandler<DecimalsFunction>();
        var symbolHandler = _web3.Eth.GetContractQueryHandler<SymbolFunction>();

        var decimalsTask = decimalsHandler.QueryAsync<byte>(tokenAddress, new DecimalsFunction());
        var symbolTask = symbolHandler.QueryAsync<string>(tokenAddress, new SymbolFunction());

        await Task.WhenAll(decimalsTask, symbolTask);

        return new TokenMetadata
        {
            Decimals = await decimalsTask,
            Symbol = await symbolTask
        };
    }

    private static string NormalizeAddress(string address) => address.Trim().ToLowerInvariant();

    private static decimal CalculatePrice(BigInteger sqrtPriceX96, byte decimals0, byte decimals1)
    {
        var numerator = BigInteger.Pow(sqrtPriceX96, 2);
        var denominator = Q192;
        var decimalsDifference = decimals0 - decimals1;

        if (decimalsDifference > 0)
        {
            numerator *= BigInteger.Pow(10, decimalsDifference);
        }
        else if (decimalsDifference < 0)
        {
            denominator *= BigInteger.Pow(10, Math.Abs(decimalsDifference));
        }

        var normalizedPrice = new BigDecimal(numerator, 0) / new BigDecimal(denominator, 0);

        return (decimal)normalizedPrice;
    }

    [Function("token0", "address")]
    private sealed class Token0Function : FunctionMessage;

    [Function("token1", "address")]
    private sealed class Token1Function : FunctionMessage;

    [Function("slot0", typeof(Slot0OutputDto))]
    private sealed class Slot0Function : FunctionMessage;

    [Function("decimals", "uint8")]
    private sealed class DecimalsFunction : FunctionMessage;

    [Function("symbol", "string")]
    private sealed class SymbolFunction : FunctionMessage;

    private sealed class TokenMetadata
    {
        public required byte Decimals { get; init; }

        public required string Symbol { get; init; }
    }

    private sealed class PoolMetadata
    {
        public required string Token0Address { get; init; }

        public required string Token1Address { get; init; }

        public required byte Token0Decimals { get; init; }

        public required byte Token1Decimals { get; init; }

        public required string Token0Symbol { get; init; }

        public required string Token1Symbol { get; init; }
    }
}
