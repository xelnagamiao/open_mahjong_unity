using UnityEngine;

public class User
{
    public string Username { get; private set; }
    // Future user-specific data can be added here (e.g., ID, email)

    public User(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            // It's good practice to handle invalid arguments.
            throw new System.ArgumentException("Username cannot be null or empty.", nameof(username));
        }
        Username = username;
    }

    public override string ToString()
    {
        return $"User(Username: {Username})";
    }
}
