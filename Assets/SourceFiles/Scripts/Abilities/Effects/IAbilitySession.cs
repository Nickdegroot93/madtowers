public interface IAbilitySession
{
    bool IsFinishing { get; }
    void CancelSession();
}
