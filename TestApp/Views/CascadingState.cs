﻿using Generated;

namespace TestApp.Views;

public class CascadingState : CascadingStateBase
{
    public CascadingState(IServiceProvider sp) : base(sp)
    {
        loggedIn = new()
        {
            Id = 1,
            FirstName = "John"
        };
    }

    override public void SetLoggedIn(Account loggedIn)
    {
        this.loggedIn = loggedIn;
    }
}
