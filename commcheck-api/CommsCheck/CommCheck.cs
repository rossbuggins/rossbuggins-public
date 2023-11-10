using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RulesEngine;
public class CommCheck(
    CommsCheckItemSha sha,
    IServiceProvider _services,
    ILogger<CommCheck> _logger) : ICommCheck
{
    public Task<CommsCheckAnswer> Check(CommsCheckItem item)
    {
        var id = sha.GetSha(item);
        var str = item.ToString();
        var app = CheckRules<App>(item);
        var sms = CheckRules<Sms>(item);
        var email = CheckRules<Email>(item);
        var postal = CheckRules<Postal>(item);
        return Task.FromResult(new CommsCheckAnswer(id, str, app, email, sms, postal));
    }

    private IRuleOutcome CheckRules<T>(CommsCheckItem item) where T : IContactType
    {
        var ruleCheck = GetRuleChecks<T>(item);
        LogFinalRule<T>(item, ruleCheck);
        return ruleCheck;
    }
    
    private IRuleOutcome GetRuleChecks<T>(CommsCheckItem item) where T : IContactType
    {
        var explicityBlock = ExplicitBlock<T>(item);
        if (explicityBlock.IsBlocked())
            return explicityBlock;

        var allow = ChannelAllow<T>(item);
        if (allow.IsAllowed())
            return allow;

        return IRuleOutcome.Blocked("Default block due to no rules found.");
    }

    private IRuleOutcome ExplicitBlock<T>(CommsCheckItem item) where T : IContactType
    {
        var generalBlock = AnyChannelExplicitBlock(item);
        if (generalBlock.IsBlocked())
            return generalBlock;

        var channelSpecific = ChannelSpecificExplicitBlock<T>(item);
        return channelSpecific;
    }

    private IRuleOutcome ChannelAllow<T>(CommsCheckItem item) where T : IContactType
    {
        return RunSpecificRules<T>(item, (rule, itm) => rule.Allowed(item));
    }

    private IRuleOutcome ChannelSpecificExplicitBlock<T>(CommsCheckItem item) where T : IContactType
    {
        return RunSpecificRules<T>(item, (rule, itm) => rule.Block(item));
    }

    private IEnumerable<ICommCheckRule> AllChannelRules()
    {
        return _services.GetServices<ICommCheckRule>();
    }

    public IEnumerable<ICommCheckRule<T>> ChannelTRules<T>() where T : IContactType
    {
        return _services.GetServices<ICommCheckRule<T>>();
    }

    private IRuleOutcome AnyChannelExplicitBlock(CommsCheckItem item)
    {
        var rules = AllChannelRules();
        return RunRules(rules.Select(r => r.Block(item)));
    }

    private IRuleOutcome RunSpecificRules<T>(CommsCheckItem item, Func<ICommCheckRule<T>, CommsCheckItem, IRuleOutcome> rule) where T : IContactType
    {
        var channelSpecificRules = ChannelTRules<T>();
        return RunRules(channelSpecificRules.Select(r => rule(r, item)));
    }

    private IRuleOutcome RunRules(IEnumerable<IRuleOutcome> ruleRun)
    {
        var outcomes = ExecuteRules(ruleRun);
        LogRules(outcomes);
        return CheckRules(outcomes);
    }

    private IList<IRuleOutcome> ExecuteRules(IEnumerable<IRuleOutcome> ruleRun)
    {
        return ruleRun.ToList();
    }

    private IRuleOutcome CheckRules(IEnumerable<IRuleOutcome> outcomes)
    {
        if (outcomes.Any(x => x.IsBlocked()))
        {
            var blockedReasons = outcomes.Where(x => x.IsBlocked()).Select(x => x.Reason).ToList();
            return IRuleOutcome.Blocked(String.Join(", ", blockedReasons));
        }

        if (outcomes.Any(x => x.IsAllowed()))
        {
            var allowedReasons = outcomes.Where(x => x.IsAllowed()).Select(x => x.Reason).ToList();
            return IRuleOutcome.Allowed(String.Join(", ", allowedReasons));
        }

        return IRuleOutcome.Ignored();
    }

    private void LogRules(IEnumerable<IRuleOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
            LogSingleRule(outcome);
    }

    private void LogSingleRule(IRuleOutcome outcome)
    {
        if(outcome.IsBlocked())
            _logger.LogWarning(outcome.ToString());
        else if(outcome.IsAllowed())
            _logger.LogInformation(outcome.ToString());
        else
            _logger.LogDebug(outcome.ToString());
    }

        private void LogFinalRule<T>(
            CommsCheckItem item, 
            IRuleOutcome outcome) where T:IContactType
    {
        _logger.LogInformation(
            "Final Check on {T} for item {item} with outcome {outcome}", 
            typeof(T).Name, 
            item, 
            outcome.ToString());
    }


}