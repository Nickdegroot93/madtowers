-- get_profile: the client's one-call identity read (display name + link state).
-- Agent contract: OnlineService boot fetches this after auth; shape must stay
-- {"display_name": text, "is_linked": bool}.

create or replace function public.get_profile()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_result jsonb;
begin
  select jsonb_build_object(
    'display_name', p.display_name,
    'is_linked',    p.is_linked
  )
  into v_result
  from public.profiles p
  where p.user_id = auth.uid();

  if v_result is null then
    -- Trigger should have created the row at signup; self-heal if it is missing.
    insert into public.profiles (user_id, display_name, is_linked)
    values (auth.uid(), 'Builder-' || lpad((abs(hashtext(auth.uid()::text)) % 10000)::text, 4, '0'), false)
    on conflict (user_id) do nothing;

    select jsonb_build_object('display_name', p.display_name, 'is_linked', p.is_linked)
    into v_result
    from public.profiles p
    where p.user_id = auth.uid();
  end if;

  return v_result;
end;
$$;

revoke all on function public.get_profile() from public, anon;
grant execute on function public.get_profile() to authenticated, service_role;
